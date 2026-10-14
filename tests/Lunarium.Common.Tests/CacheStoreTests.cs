using Lunarium.Common.Caching;

namespace Lunarium.Common.Tests;

public class CacheStoreTests
{
    [Fact]
    public async Task SetGetRemove_WorkThroughPublicApi()
    {
        using var storage = new TempStorage();
        await using var store = new CacheStore(TestHelpers.UniqueNamespace(), storageDirectory: storage.Root);

        store.Set("i", 7);
        store.Set("s", "文本");

        Assert.Equal(7, store.Get<int>("i"));
        Assert.Equal("文本", store.Get<string>("s"));
        Assert.True(store.Remove("i"));
        Assert.False(store.Remove("i"));
        Assert.Equal(0, store.Get<int>("i"));
        Assert.Equal(-1, store.Get("i", -1));
    }

    [Fact]
    public async Task Set_UnsupportedType_Throws()
    {
        using var storage = new TempStorage();
        await using var store = new CacheStore(TestHelpers.UniqueNamespace(), storageDirectory: storage.Root);

        Assert.Throws<NotSupportedException>(() => store.Set("dt", DateTime.UtcNow));
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        using var storage = new TempStorage();

        Assert.Throws<ArgumentNullException>(() => new CacheStore(null!));
        Assert.Throws<ArgumentException>(() => new CacheStore(string.Empty));
        Assert.Throws<ArgumentException>(() => new CacheStore("非法/名称"));
        Assert.Throws<ArgumentNullException>(
            () => new CacheStore(TestHelpers.UniqueNamespace(), logError: null, allowCorruptRebuild: true));
        Assert.Throws<ArgumentException>(
            () => new CacheStore(TestHelpers.UniqueNamespace(), storageDirectory: "   "));
    }

    [Fact]
    public async Task Constructor_DuplicateNamespace_Throws()
    {
        using var storage = new TempStorage();
        string ns = TestHelpers.UniqueNamespace();

        await using var first = new CacheStore(ns, storageDirectory: storage.Root);
        Assert.Throws<InvalidOperationException>(() => new CacheStore(ns, storageDirectory: storage.Root));
    }

    [Fact]
    public async Task DisposeAsync_ReleasesNamespaceAndBlocksFurtherUse()
    {
        using var storage = new TempStorage();
        string ns = TestHelpers.UniqueNamespace();

        var store = new CacheStore(ns, storageDirectory: storage.Root);
        await store.DisposeAsync();

        // 命名空间占位已释放 → 可再次创建同名实例
        await using var again = new CacheStore(ns, storageDirectory: storage.Root);
        Assert.Equal(0, again.Get<int>("k"));

        Assert.Throws<ObjectDisposedException>(() => store.Set("k", 1));
        Assert.Throws<ObjectDisposedException>(() => store.Get<int>("k"));
        Assert.Throws<ObjectDisposedException>(() => store.Remove("k"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.FlushAsync());
    }

    [Fact]
    public async Task FlushAsync_WritesToInjectedDirectoryAsIndentedUnescapedJson()
    {
        using var storage = new TempStorage();
        string ns = TestHelpers.UniqueNamespace();
        string path = storage.FileFor(ns);

        await using (var store = new CacheStore(ns, storageDirectory: storage.Root))
        {
            store.Set("name", "月球观测站");
            store.Set("count", 3);
            await store.FlushAsync();
        }

        Assert.True(File.Exists(path), $"缓存文件未落在注入的目录中: {path}");
        Assert.False(File.Exists(path + ".tmp"));

        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("月球观测站", json);        // Encoder 生效
        Assert.DoesNotContain("\\u", json);
        Assert.Contains("\n  ", json);             // WriteIndented 生效
        Assert.DoesNotContain("_index", json);     // 派生索引不落盘
    }

    [Fact]
    public async Task Reload_RoundTripsAllSupportedTypes()
    {
        using var storage = new TempStorage();
        string ns = TestHelpers.UniqueNamespace();

        await using (var store = new CacheStore(ns, storageDirectory: storage.Root))
        {
            store.Set("s", "中文");
            store.Set("i", -5);
            store.Set("l", 1234567890123L);
            store.Set("sh", (short)12);
            store.Set("ui", 7u);
            store.Set("ul", ulong.MaxValue);
            store.Set("d", 2.5d);
            store.Set("f", 0.25f);
            store.Set("b", true);
            await store.FlushAsync();
        }

        await using var reopened = new CacheStore(ns, storageDirectory: storage.Root);

        Assert.Equal("中文", reopened.Get<string>("s"));
        Assert.Equal(-5, reopened.Get<int>("i"));
        Assert.Equal(1234567890123L, reopened.Get<long>("l"));
        Assert.Equal((short)12, reopened.Get<short>("sh"));
        Assert.Equal(7u, reopened.Get<uint>("ui"));
        Assert.Equal(ulong.MaxValue, reopened.Get<ulong>("ul"));
        Assert.Equal(2.5d, reopened.Get<double>("d"));
        Assert.Equal(0.25f, reopened.Get<float>("f"));
        Assert.True(reopened.Get<bool>("b"));
        Assert.Equal(0, reopened.Get<int>("missing"));
    }

    [Fact]
    public async Task Constructor_FromCorruptFile_ThrowsAndReleasesNamespace()
    {
        using var storage = new TempStorage();
        string ns = TestHelpers.UniqueNamespace();
        string path = storage.FileFor(ns);
        Directory.CreateDirectory(storage.Directory);
        await File.WriteAllTextAsync(path, "{ 这不是合法 JSON");

        Assert.Throws<CacheFileCorruptException>(
            () => new CacheStore(ns, storageDirectory: storage.Root));

        // 构造失败必须释放命名空间占位，否则调用方修正文件后也无法重试
        await File.WriteAllTextAsync(path, "{}");
        await using var store = new CacheStore(ns, storageDirectory: storage.Root);
        Assert.Equal(0, store.Get<int>("missing"));
    }

    [Fact]
    public async Task Constructor_AllowCorruptRebuild_LogsArchivesAndStartsEmpty()
    {
        using var storage = new TempStorage();
        string ns = TestHelpers.UniqueNamespace();
        string path = storage.FileFor(ns);
        Directory.CreateDirectory(storage.Directory);
        await File.WriteAllTextAsync(path, "完全不是 JSON");

        var errors = new List<Exception>();
        await using (var store = new CacheStore(ns, errors.Add, allowCorruptRebuild: true, storageDirectory: storage.Root))
        {
            Assert.Single(errors);
            Assert.IsType<CacheFileCorruptException>(errors[0]);
            Assert.Equal(0, store.Get<int>("missing"));   // 以空缓存继续

            store.Set("k", 1);
            await store.FlushAsync();
        }

        string[] archived = Directory.GetFiles(storage.Directory, ns + ".json.corrupt-*");
        Assert.Single(archived);
        Assert.True(File.Exists(path));                   // 重建后的新文件
    }

    [Fact]
    public async Task GetOrSet_AcrossManyKeys_IsConsistent()
    {
        using var storage = new TempStorage();
        await using var store = new CacheStore(TestHelpers.UniqueNamespace(), storageDirectory: storage.Root);

        for (int i = 0; i < 500; i++)
            store.Set("k" + i, i);

        for (int i = 0; i < 500; i++)
            Assert.Equal(i, store.Get<int>("k" + i));

        for (int i = 0; i < 500; i += 2)
            Assert.True(store.Remove("k" + i));

        for (int i = 0; i < 500; i++)
            Assert.Equal(i % 2 == 0 ? 0 : i, store.Get<int>("k" + i));
    }

    [Fact]
    public async Task RelativeStorageDirectory_IsResolvedToAbsolutePath()
    {
        using var storage = new TempStorage();
        string ns = TestHelpers.UniqueNamespace();

        // 传相对路径：应基于「进程当前目录」解析，且不受后续 CWD 变化影响
        string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), storage.Root);

        await using (var store = new CacheStore(ns, storageDirectory: relative))
        {
            store.Set("k", 1);
            await store.FlushAsync();
        }

        Assert.True(File.Exists(storage.FileFor(ns)));
    }

    [Fact]
    public async Task DifferentStorageDirectories_StillShareNamespaceRegistry()
    {
        using var storageA = new TempStorage();
        using var storageB = new TempStorage();
        string ns = TestHelpers.UniqueNamespace();

        // 命名空间注册表是进程级的：即使存储目录不同，同名命名空间仍应被拒绝，
        // 避免两个实例写同一份数据却互不知情。
        await using var first = new CacheStore(ns, storageDirectory: storageA.Root);
        Assert.Throws<InvalidOperationException>(
            () => new CacheStore(ns, storageDirectory: storageB.Root));
    }
}
