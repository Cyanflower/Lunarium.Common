namespace Lunarium.Common.Examples;

internal static class ExampleSupport
{
    public static string CreateStorageDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "Lunarium.Common.Examples",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteStorageDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: could not delete temporary directory {path}: {ex.Message}");
        }
    }

    public static void Section(string title) => Console.WriteLine($"\n=== {title} ===");
}
