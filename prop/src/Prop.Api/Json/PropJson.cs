using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Prop.Api.Challenges;
using Prop.Rules;

namespace Prop.Api.Json;

/// <summary>
/// JSON settings for the API and the database: camelCase, enums as strings, and the rule engine's abstract
/// types and the queued trading commands written with a "kind" property naming the concrete type, like on
/// the trading platform.
/// Kept here so the rule engine stays free of serialization concerns.
/// </summary>
internal static class PropJson
{
    public const string KindProperty = "kind";

    private static readonly Type[] PolymorphicBases = [typeof(ChallengeInput), typeof(ChallengeOutput), typeof(FloorSpec), typeof(TradingCommand)];

    private static readonly Type[] KnownTypes = [.. typeof(ChallengeRules).Assembly.GetTypes(), .. typeof(PropJson).Assembly.GetTypes()];

    public static JsonSerializerOptions Options { get; } = CreateOptions();

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
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver()).WithAddedModifier(AddRulePolymorphism);
    }

    private static void AddRulePolymorphism(JsonTypeInfo typeInfo)
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
        foreach (var derived in KnownTypes.Where(t => t.IsSealed && t.IsAssignableTo(typeInfo.Type)).OrderBy(t => t.Name, StringComparer.Ordinal))
        {
            polymorphism.DerivedTypes.Add(new JsonDerivedType(derived, derived.Name));
        }

        typeInfo.PolymorphismOptions = polymorphism;
    }
}
