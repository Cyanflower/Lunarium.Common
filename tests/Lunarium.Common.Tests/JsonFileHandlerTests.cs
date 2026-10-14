using Lunarium.Common.Caching;

namespace Lunarium.Common.Tests;

public class JsonFileHandlerTests : IDisposable
{
    private readonly string _dir = TestHelpers.NewTempDirectory();

    private string PathFor(string name) => Path.Combine(_dir, name);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task SaveToFileAsync_CreatesDirectoryAndLeavesNoTempFile()
    {
        string path = PathFor(Path.Combine("nested", "cache.json"));
        var handler = new JsonFileHandler(path);

        var cache = new Cache();
        cache.Set("k", "值");
        await handler.SaveToFileAsync(cache);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));           // 原子写：临时文件已被改名
    }

    [Fact]
    public async Task SaveToFileAsync_WritesIndentedJsonWithoutAsciiEscaping()
    {
        string path = PathFor("cache.json");
        var handler = new JsonFileHandler(path);

        var cache = new Cache();
        cache.Set("name", "月球观测站");
        await handler.SaveToFileAsync(cache);

        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\n  ", json);                      // WriteIndented 生效
        Assert.Contains("月球观测站", json);                 // Encoder 生效：非 ASCII 未转义
        Assert.DoesNotContain("\\u", json);
        Assert.DoesNotContain("_index", json);              // 派生索引不参与序列化
    }

    [Fact]
    public void LoadFromFile_MissingFile_ReturnsEmptyCache()
    {
        var handler = new JsonFileHandler(PathFor("missing.json"));
        var cache = handler.LoadFromFile();

        Assert.NotNull(cache);
        Assert.Empty(cache.Strings);
        Assert.Null(cache.Get<string>("any"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void LoadFromFile_EmptyOrWhitespaceContent_ReturnsEmptyCache(string content)
    {
        string path = PathFor("empty.json");
        File.WriteAllText(path, content);

        var cache = new JsonFileHandler(path).LoadFromFile();

        Assert.NotNull(cache);
        Assert.Empty(cache.Strings);
    }

    [Theory]
    [InlineData("{ 不是 json")]
    [InlineData("{\"Strings\": 123}")]                      // 结构不匹配
    public void LoadFromFile_CorruptContent_ThrowsCacheFileCorruptException(string content)
    {
        string path = PathFor("corrupt.json");
        File.WriteAllText(path, content);

        Assert.Throws<CacheFileCorruptException>(() => new JsonFileHandler(path).LoadFromFile());
    }

    [Fact]
    public async Task LoadFromFile_RoundTripsAndRebuildsIndex()
    {
        string path = PathFor("roundtrip.json");
        var handler = new JsonFileHandler(path);

        var cache = new Cache();
        cache.Set("s", "文本");
        cache.Set("n", 3);
        await handler.SaveToFileAsync(cache);

        var reloaded = new JsonFileHandler(path).LoadFromFile();

        Assert.Equal("文本", reloaded.Get<string>("s"));     // 加载后索引已重建
        Assert.Equal(3L, reloaded.Get<long>("n"));
        Assert.True(reloaded.Remove("s"));                  // 索引可用于精确定位
    }

    [Fact]
    public void ArchiveCorruptFile_MissingFile_ReturnsNull()
        => Assert.Null(new JsonFileHandler(PathFor("none.json")).ArchiveCorruptFile());

    [Fact]
    public void ArchiveCorruptFile_RenamesFileAndReturnsArchivePath()
    {
        string path = PathFor("bad.json");
        File.WriteAllText(path, "坏内容");
        var handler = new JsonFileHandler(path);

        string? archived = handler.ArchiveCorruptFile();

        Assert.NotNull(archived);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(archived));
        Assert.Contains(".corrupt-", archived);
        Assert.Equal("坏内容", File.ReadAllText(archived!));
    }

    [Fact]
    public async Task SaveToFileAsync_CancelledToken_Throws()
    {
        var handler = new JsonFileHandler(PathFor("cancel.json"));
        var cache = new Cache();
        cache.Set("k", 1);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.SaveToFileAsync(cache, cts.Token));
    }

    [Fact]
    public async Task SaveToFileAsync_ConcurrentCalls_ProduceValidJson()
    {
        string path = PathFor("concurrent.json");
        var handler = new JsonFileHandler(path);

        var tasks = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            var snapshot = new Cache();
            snapshot.Set("n", i);
            tasks.Add(handler.SaveToFileAsync(snapshot));
        }
        await Task.WhenAll(tasks);

        // 原子改名 + 信号量 保证文件始终是完整 JSON，可被正常解析
        var loaded = new JsonFileHandler(path).LoadFromFile();
        Assert.InRange(loaded.Get<int>("n"), 0, 19);
    }
}
