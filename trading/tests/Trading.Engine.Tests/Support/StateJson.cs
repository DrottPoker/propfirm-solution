using System.Text.Json;

namespace Trading.Engine.Tests.Support;

/// <summary>Engine state as JSON, for comparing two states.</summary>
internal static class StateJson
{
    public static string Serialize(EngineState state) => JsonSerializer.Serialize(state, EventJson.SharedOptions);
}
