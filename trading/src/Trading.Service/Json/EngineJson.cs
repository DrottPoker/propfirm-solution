using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Trading.Engine;
using Trading.Engine.Events;
using Trading.Engine.Inputs;

namespace Trading.Service.Json;

/// <summary>
/// JSON settings shared by the REST API and SignalR: camelCase, enums as strings, and
/// abstract engine types written with a "kind" property naming the concrete type.
/// Kept here so the engine stays free of serialization concerns.
/// </summary>
internal static class EngineJson
{
    public const string KindProperty = "kind";

    private static readonly Type[] PolymorphicBases = [typeof(EngineEvent), typeof(EngineInput), typeof(EquityFloorRule)];

    private static readonly Type[] EngineTypes = typeof(TradingEngine).Assembly.GetTypes();

    public static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    public static void Configure(JsonSerializerOptions options)
    {
        // Numbers are JSON numbers only. The web defaults would also accept numbers written as strings.
        options.NumberHandling = JsonNumberHandling.Strict;
        options.Converters.Add(new JsonStringEnumConverter());
        options.AllowOutOfOrderMetadataProperties = true;
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver()).WithAddedModifier(AddEnginePolymorphism);
    }

    private static void AddEnginePolymorphism(JsonTypeInfo typeInfo)
    {
        if (!PolymorphicBases.Contains(typeInfo.Type))
        {
            return;
        }

        var polymorphism = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = KindProperty,
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FailSerialization,
        };
        foreach (var derived in EngineTypes.Where(t => t.IsSealed && t.IsAssignableTo(typeInfo.Type)).OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            polymorphism.DerivedTypes.Add(new JsonDerivedType(derived, derived.Name));
        }

        typeInfo.PolymorphismOptions = polymorphism;
    }
}
