using Lunarium.Common;
using Lunarium.Common.Net;

namespace Lunarium.Common.Examples;

internal static class EnvExample
{
    private const string ModeName = "LUNARIUM_COMMON_EXAMPLE_MODE";
    private const string NumberName = "LUNARIUM_COMMON_EXAMPLE_NUMBER";
    private const string ProxyName = "LUNARIUM_COMMON_EXAMPLE_PROXY";
    private const string UrlName = "LUNARIUM_COMMON_EXAMPLE_API_URL";

    public static void Run()
    {
        ExampleSupport.Section("EnvVar / ProxyUrl / EnvUrl");

        try
        {
            EnvVar.Set(ModeName, " production ");
            EnvVar.Set(NumberName, "42");
            EnvVar.Set(ProxyName, "http://127.0.0.1:8080");
            EnvVar.Set(UrlName, "https://api.example.com/base/");

            EnvVar? mode = EnvVar.Get(ModeName);
            Console.WriteLine($"MODE = {mode?.Value} (ToString = {mode})");
            Console.WriteLine($"number = {EnvVar.GetRequired(NumberName).As(30)}");
            Console.WriteLine($"isProduction = {EnvVar.GetRequired(ModeName).AsBoolean("production", "development")}");

            ProxyUrl? proxy = ProxyUrl.FromEnvironment(ProxyName);
            Console.WriteLine($"proxy = {proxy}, uri = {proxy?.ToUri()}");

            EnvUrl? apiBase = EnvUrl.FromEnvironment(UrlName);
            Console.WriteLine($"api base = {apiBase}, uri = {apiBase?.ToUri()}");

            Console.WriteLine($"missing = {EnvVar.Get("LUNARIUM_COMMON_EXAMPLE_MISSING")?.Value ?? "<null>"}");
        }
        finally
        {
            EnvVar.Unset(ModeName);
            EnvVar.Unset(NumberName);
            EnvVar.Unset(ProxyName);
            EnvVar.Unset(UrlName);
        }
    }
}
