using Lunarium.Common.Examples;

string command = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

try
{
    switch (command)
    {
        case "env":
            EnvExample.Run();
            break;

        case "ids":
            IdExample.Run();
            break;

        case "debounce":
            await DebounceExample.RunAsync();
            break;

        case "cache":
            await CacheExample.RunAsync();
            break;

        case "document":
            await DocumentExample.RunAsync();
            break;

        case "all":
            EnvExample.Run();
            IdExample.Run();
            await DebounceExample.RunAsync();
            await CacheExample.RunAsync();
            await DocumentExample.RunAsync();
            break;

        case "help":
        case "-h":
        case "--help":
            PrintHelp();
            return 0;

        default:
            Console.Error.WriteLine($"Unknown command: {command}");
            PrintHelp();
            return 1;
    }

    Console.WriteLine("\nAll examples completed.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Example failed: {ex}");
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
        Lunarium.Common examples

        Usage:
          dotnet run --project docs/examples/Lunarium.Common.Examples.csproj -- <command>

        Commands:
          all        Run every example (default)
          env        EnvVar / ProxyUrl / EnvUrl
          ids        NanoId / LocalSnowflake
          debounce   CtsDebouncer / DirtyDebouncer
          cache      CacheStore
          document   JsonDocumentStore<T>
          help       Show this help
        """);
}
