using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Lunarium.Common.Caching;

namespace Lunarium.Common.Tests;

/// <summary>
/// <see cref="JsonDocumentStore{T}"/> 的行为测试。
/// </summary>
public class JsonDocumentStoreTests
{
    // 与库内 CacheJsonContext 相同的 options 传递方式: Encoder 没有特性参数, 只能经上下文构造函数传入
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly TestDocumentJsonContext Context = new(JsonOptions);

    private static JsonTypeInfo<TestDocument> TypeInfo => Context.TestDocument;

    [Fact]
    public async Task SetRead_RoundTripsComplexDocument()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();
        const ulong snowflake = 1234567890123456789UL; // > 2^53

        await using (var store = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root))
        {
            var document = new TestDocument
            {
                Name = "中文配置",
                Snowflake = snowflake,
                Count = 7,
                Values = { 1, 2, 3 },
                Sections =
                {
                    [snowflake] = new TestSection { Label = "服务器", Ids = { 5, 6 } },
                },
            };
            store.Set(document);
            await store.FlushAsync();
        }

        await using var reopened = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root);
        TestDocument loaded = reopened.Read();

        Assert.Equal("中文配置", loaded.Name);
        Assert.Equal(snowflake, loaded.Snowflake);
        Assert.Equal(7, loaded.Count);
        Assert.Equal(new[] { 1, 2, 3 }, loaded.Values);
        Assert.True(loaded.Sections.ContainsKey(snowflake));
        Assert.Equal("服务器", loaded.Sections[snowflake].Label);
        Assert.Equal(new ulong[] { 5, 6 }, loaded.Sections[snowflake].Ids);
    }

    [Fact]
    public async Task Read_ReturnsIndependentSnapshot()
    {
        using var storage = new TempStorage();
        await using var store = new JsonDocumentStore<TestDocument>(
            TestHelpers.UniqueNamespace(), TypeInfo, storageDirectory: storage.Root);

        store.Set(new TestDocument { Name = "原始", Values = { 1 } });

        TestDocument first = store.Read();
        TestDocument second = store.Read();

        Assert.NotSame(first, second);
        first.Name = "被修改";
        first.Values.Add(2);

        await store.FlushAsync();
        Assert.Equal("原始", store.Read().Name);
        Assert.Single(store.Read().Values);
    }

    [Fact]
    public async Task Set_StoresSnapshotOfCallerInstance()
    {
        using var storage = new TempStorage();
        await using var store = new JsonDocumentStore<TestDocument>(
            TestHelpers.UniqueNamespace(), TypeInfo, storageDirectory: storage.Root);

        var document = new TestDocument { Name = "写入时" };
        store.Set(document);
        document.Name = "写入后";

        await store.FlushAsync();
        Assert.Equal("写入时", store.Read().Name);
    }

    [Fact]
    public async Task Read_DoesNotPreserveJsonIgnoredMembers()
    {
        using var storage = new TempStorage();
        await using var store = new JsonDocumentStore<TestDocument>(
            TestHelpers.UniqueNamespace(), TypeInfo, storageDirectory: storage.Root);

        store.Set(new TestDocument { Name = "x", RuntimeOnly = "不落盘" });

        await store.FlushAsync();
        // 快照通过 JSON 往返生成, [JsonIgnore] 成员与落盘行为一致地不参与
        Assert.Equal(string.Empty, store.Read().RuntimeOnly);
    }

    [Fact]
    public async Task Update_AppliesTransformAndPersists()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();

        await using (var store = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root))
        {
            store.Set(new TestDocument { Count = 1 });
            store.Update(document => { document.Count = 2; return document; });
            Assert.Equal(2, store.Read().Count);
        }

        await using var reopened = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root);
        Assert.Equal(2, reopened.Read().Count);
    }

    [Fact]
    public async Task Update_WhenTransformThrows_LeavesDocumentUnchanged()
    {
        using var storage = new TempStorage();
        await using var store = new JsonDocumentStore<TestDocument>(
            TestHelpers.UniqueNamespace(), TypeInfo, storageDirectory: storage.Root);

        store.Set(new TestDocument { Count = 1 });

        Assert.Throws<InvalidOperationException>(
            () => store.Update(_ => throw new InvalidOperationException("校验失败")));

        await store.FlushAsync();
        Assert.Equal(1, store.Read().Count);
    }

    [Fact]
    public async Task Update_WhenTransformReturnsNull_ThrowsAndLeavesDocumentUnchanged()
    {
        using var storage = new TempStorage();
        await using var store = new JsonDocumentStore<TestDocument>(
            TestHelpers.UniqueNamespace(), TypeInfo, storageDirectory: storage.Root);

        store.Set(new TestDocument { Count = 1 });

        Assert.Throws<InvalidOperationException>(() => store.Update(_ => null!));
        await store.FlushAsync();
        Assert.Equal(1, store.Read().Count);
    }

    [Fact]
    public async Task FlushAsync_WritesIndentedUnescapedJsonWithoutTempFile()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();
        string path = storage.FileFor(name);

        await using (var store = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root))
        {
            store.Set(new TestDocument { Name = "月球观测站", Count = 3 });
            await store.FlushAsync();
        }

        Assert.True(File.Exists(path), $"文档文件未落在注入的目录中: {path}");
        Assert.False(File.Exists(path + ".tmp"));

        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("月球观测站", json);        // Encoder 生效
        Assert.DoesNotContain("\\u", json);
        Assert.Contains("\n  ", json);             // WriteIndented 生效
        Assert.DoesNotContain("RuntimeOnly", json); // [JsonIgnore] 不落盘
        Assert.False(Directory.Exists(Path.Combine(storage.Root, JsonDocumentStore<TestDocument>.DataDirectoryName)));
    }

    [Fact]
    public async Task Constructor_MissingOrEmptyFile_StartsFromNewInstance()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();

        await using (var missing = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root))
        {
            Assert.Equal(42, missing.Read().Count);   // 类内默认值
            Assert.Empty(missing.Read().Values);
        }

        File.WriteAllText(storage.FileFor(name), "  \r\n\t ");
        await using var empty = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root);
        Assert.Equal(42, empty.Read().Count);
    }

    [Fact]
    public async Task Constructor_CorruptFile_ThrowsAndReleasesPath()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();
        string path = storage.FileFor(name);
        Directory.CreateDirectory(storage.Directory);
        await File.WriteAllTextAsync(path, "{ 不是合法 JSON");

        Assert.Throws<CacheFileCorruptException>(
            () => new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root));

        // 构造失败必须释放路径占位，否则调用方修正文件后也无法重试
        await File.WriteAllTextAsync(path, "{}");
        await using var store = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root);
        Assert.Equal(42, store.Read().Count);
    }

    [Fact]
    public async Task Constructor_JsonNullRoot_ThrowsCorruptAndReleasesPath()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();
        Directory.CreateDirectory(storage.Directory);
        await File.WriteAllTextAsync(storage.FileFor(name), "null");

        Assert.Throws<CacheFileCorruptException>(
            () => new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root));

        await File.WriteAllTextAsync(storage.FileFor(name), "{}");
        await using var store = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root);
        Assert.Equal(42, store.Read().Count);
    }

    [Fact]
    public async Task Constructor_AllowCorruptRebuild_LogsArchivesAndStartsEmpty()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();
        string path = storage.FileFor(name);
        Directory.CreateDirectory(storage.Directory);
        await File.WriteAllTextAsync(path, "完全不是 JSON");

        var errors = new List<Exception>();
        await using (var store = new JsonDocumentStore<TestDocument>(
            name, TypeInfo, errors.Add, allowCorruptRebuild: true, storageDirectory: storage.Root))
        {
            Assert.Single(errors);
            Assert.IsType<CacheFileCorruptException>(errors[0]);
            Assert.Equal(42, store.Read().Count);   // 以空文档继续

            store.Update(document => { document.Count = 1; return document; });
            await store.FlushAsync();
        }

        string[] archived = Directory.GetFiles(storage.Directory, name + ".json.corrupt-*");
        Assert.Single(archived);
        Assert.True(File.Exists(path));             // 重建后的新文件
    }

    [Fact]
    public async Task DisposeAsync_FlushesDirtyDataAndReleasesPath()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();

        var store = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root);
        store.Set(new TestDocument { Count = 9 });
        await store.DisposeAsync();   // 未显式 Flush, 释放时必须落盘

        await using (var reopened = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root))
        {
            Assert.Equal(9, reopened.Read().Count);
        }

        Assert.Throws<ObjectDisposedException>(() => store.Read());
        Assert.Throws<ObjectDisposedException>(() => store.Set(new TestDocument()));
        Assert.Throws<ObjectDisposedException>(() => store.Update(document => document));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.FlushAsync());
    }

    [Fact]
    public async Task Constructor_DuplicatePath_Throws()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();

        await using var first = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root);
        Assert.Throws<InvalidOperationException>(
            () => new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root));

        await first.FlushAsync();
    }

    [Fact]
    public async Task Constructor_PathOccupiedByCacheStore_ThrowsUntilReleased()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();

        // 两类存储共享同一张路径注册表: 同一个 {name}.json 不能被两种格式同时写入
        var cache = new CacheStore(name, storageDirectory: storage.Root);
        try
        {
            Assert.Throws<InvalidOperationException>(
                () => new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root));
        }
        finally
        {
            await cache.DisposeAsync();
        }

        await using var store = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: storage.Root);
        Assert.Equal(42, store.Read().Count);
    }

    [Fact]
    public async Task RelativeStorageDirectory_IsResolvedAgainstBaseDirectory()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();

        // 相对路径以 AppContext.BaseDirectory 为基准（与默认位置一致），而不是进程当前目录
        string relative = Path.GetRelativePath(AppContext.BaseDirectory, storage.Root);

        await using (var store = new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: relative))
        {
            store.Set(new TestDocument { Count = 1 });
            await store.FlushAsync();
        }

        Assert.True(File.Exists(storage.FileFor(name)));
    }

    [Fact]
    public void Constructor_ValidatesArguments()
    {
        using var storage = new TempStorage();
        string name = TestHelpers.UniqueNamespace();

        Assert.Throws<ArgumentNullException>(() => new JsonDocumentStore<TestDocument>(null!, TypeInfo));
        Assert.Throws<ArgumentException>(() => new JsonDocumentStore<TestDocument>(string.Empty, TypeInfo));
        Assert.Throws<ArgumentException>(() => new JsonDocumentStore<TestDocument>("非法/名称", TypeInfo));
        Assert.Throws<ArgumentNullException>(() => new JsonDocumentStore<TestDocument>(name, null!));
        Assert.Throws<ArgumentException>(
            () => new JsonDocumentStore<TestDocument>(name, TypeInfo, storageDirectory: "   "));
        Assert.Throws<ArgumentNullException>(
            () => new JsonDocumentStore<TestDocument>(name, TypeInfo, logError: null, allowCorruptRebuild: true));
    }

    [Fact]
    public async Task Set_Null_Throws()
    {
        using var storage = new TempStorage();
        await using var store = new JsonDocumentStore<TestDocument>(
            TestHelpers.UniqueNamespace(), TypeInfo, storageDirectory: storage.Root);

        Assert.Throws<ArgumentNullException>(() => store.Set(null!));
        Assert.Throws<ArgumentNullException>(() => store.Update(null!));

        await store.FlushAsync();
    }
}

/// <summary>
/// 测试用文档根类型，覆盖字典键为 <see cref="ulong"/>、集合与默认值等场景。
/// </summary>
internal sealed class TestDocument
{
    public string Name { get; set; } = string.Empty;

    public ulong Snowflake { get; set; }

    public int Count { get; set; } = 42;

    public List<int> Values { get; set; } = new();

    public Dictionary<ulong, TestSection> Sections { get; set; } = new();

    [JsonIgnore]
    public string RuntimeOnly { get; set; } = string.Empty;
}

/// <summary>
/// 测试用嵌套配置类型。
/// </summary>
internal sealed class TestSection
{
    public string Label { get; set; } = string.Empty;

    public List<ulong> Ids { get; set; } = new();
}

/// <summary>
/// 测试用源生成上下文：模拟消费方为文档根类型提供 <see cref="JsonTypeInfo{T}"/>。
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(TestDocument))]
internal partial class TestDocumentJsonContext : JsonSerializerContext;
