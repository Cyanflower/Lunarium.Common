using System.Text.Json.Serialization;

namespace Lunarium.Common.Examples;

public sealed class BotConfig
{
    public string Name { get; set; } = "";

    public int RetryCount { get; set; } = 3;

    [JsonIgnore]
    public string RuntimeOnly { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BotConfig))]
internal partial class BotConfigJsonContext : JsonSerializerContext;
