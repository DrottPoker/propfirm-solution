using System.Text.Json;
using System.Text.Json.Serialization;

using Trading.Engine.Events;

namespace Trading.Engine.Tests.Support;

/// <summary>Stable JSON for comparing engine output. Abstract engine types are written with their runtime type name.</summary>
internal static class EventJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter(), new RuntimeTypeConverterFactory() },
    };

    public static string Serialize(EngineEvent engineEvent) => JsonSerializer.Serialize(engineEvent, Options);

    public static string Serialize(AccountSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    private sealed class RuntimeTypeConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert.IsAbstract && typeToConvert.Assembly == typeof(TradingEngine).Assembly;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(RuntimeTypeConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class RuntimeTypeConverter<T> : JsonConverter<T>
        where T : class
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            var node = JsonSerializer.SerializeToNode(value, value.GetType(), options)!.AsObject();
            writer.WriteStartObject();
            writer.WriteString("$type", value.GetType().Name);
            foreach (var (name, child) in node)
            {
                writer.WritePropertyName(name);
                if (child is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    child.WriteTo(writer, options);
                }
            }

            writer.WriteEndObject();
        }
    }
}
