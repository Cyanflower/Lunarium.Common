using System.Text;
using Lunarium.Common;
using Lunarium.Common.UID;

namespace Lunarium.Common.Examples;

internal static class IdExample
{
    public static void Run()
    {
        ExampleSupport.Section("NanoId / LocalSnowflake");

        string token = NanoId.Generate();
        string humanCode = NanoId.Generate(12, Alphabet.HumanReadable);
        byte[] utf8 = NanoId.GenerateUtf8();

        Span<byte> buffer = stackalloc byte[21];
        NanoId.GenerateUtf8(buffer);

        long id = LocalSnowflake.NextId();
        long nextId = LocalSnowflake.NextId();
        DateTimeOffset createdAt = LocalSnowflake.ExtractTime(id);
        bool expired = LocalSnowflake.IsExpired(id, TimeSpan.FromHours(24));

        Console.WriteLine($"nanoId = {token}");
        Console.WriteLine($"humanCode = {humanCode}");
        Console.WriteLine($"utf8 = {Encoding.UTF8.GetString(utf8)}");
        Console.WriteLine($"buffer = {Encoding.UTF8.GetString(buffer)}");
        Console.WriteLine($"snowflake = {id}");
        Console.WriteLine($"snowflake.createdAt = {createdAt:O}");
        Console.WriteLine($"snowflake.monotonic = {nextId > id}");
        Console.WriteLine($"snowflake.expired = {expired}");

        if (nextId <= id)
            throw new InvalidOperationException("LocalSnowflake 没有保持进程内单调递增。");

        if (expired)
            throw new InvalidOperationException("刚生成的 LocalSnowflake ID 不应过期。");
    }
}
