using Lunarium.Common.Caching;

namespace Lunarium.Common.Examples;

internal static class DocumentExample
{
    public static async Task RunAsync()
    {
        ExampleSupport.Section("JsonDocumentStore<T>");

        string storage = ExampleSupport.CreateStorageDirectory();
        string file = Path.Combine(storage, "example-config.json");

        try
        {
            await using (var store = new JsonDocumentStore<BotConfig>(
                "example-config",
                BotConfigJsonContext.Default.BotConfig,
                storageDirectory: storage))
            {
                store.Update(config =>
                {
                    config.Name = "月球观测站";
                    config.RetryCount = 5;
                    config.RuntimeOnly = "not persisted";
                    return config;
                });

                Console.WriteLine($"before flush = {store.Read().Name}");
                await store.FlushAsync();
            }

            Console.WriteLine($"file exists after dispose = {File.Exists(file)}");

            await using (var reopened = new JsonDocumentStore<BotConfig>(
                "example-config",
                BotConfigJsonContext.Default.BotConfig,
                storageDirectory: storage))
            {
                BotConfig config = reopened.Read();
                Console.WriteLine($"reloaded name = {config.Name}");
                Console.WriteLine($"reloaded retry = {config.RetryCount}");
                Console.WriteLine($"reloaded runtimeOnly = '{config.RuntimeOnly}' ([JsonIgnore])");
            }
        }
        finally
        {
            ExampleSupport.DeleteStorageDirectory(storage);
        }
    }
}
