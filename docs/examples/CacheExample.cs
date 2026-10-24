using Lunarium.Common.Caching;

namespace Lunarium.Common.Examples;

internal static class CacheExample
{
    public static async Task RunAsync()
    {
        ExampleSupport.Section("CacheStore");

        string storage = ExampleSupport.CreateStorageDirectory();
        string file = Path.Combine(storage, "example-cache.json");

        try
        {
            await using (var cache = new CacheStore("example-cache", storageDirectory: storage))
            {
                cache.Set("restart-count", 3);
                cache.Set("enabled", true);
                cache.Set("name", "月球观测站");

                Console.WriteLine($"count = {cache.Get("restart-count", 0)}");
                await cache.FlushAsync();
            }

            Console.WriteLine($"file exists after dispose = {File.Exists(file)}");

            await using (var reopened = new CacheStore("example-cache", storageDirectory: storage))
            {
                Console.WriteLine($"reloaded count = {reopened.Get("restart-count", 0)}");
                Console.WriteLine($"reloaded name = {reopened.Get<string>("name")}");
            }
        }
        finally
        {
            ExampleSupport.DeleteStorageDirectory(storage);
        }
    }
}
