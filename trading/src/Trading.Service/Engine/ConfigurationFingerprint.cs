using System.Security.Cryptography;
using System.Text.Json;

using Trading.Engine;
using Trading.Service.Json;

namespace Trading.Service.Engine;

/// <summary>Identifies an engine configuration, so a restart can tell whether it changed.</summary>
internal static class ConfigurationFingerprint
{
    private static readonly JsonSerializerOptions Json = EngineJson.CreateOptions();

    public static string Of(EngineConfiguration configuration) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(configuration, Json)));
}
