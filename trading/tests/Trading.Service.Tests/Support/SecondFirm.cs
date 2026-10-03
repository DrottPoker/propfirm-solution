using Trading.Service.Tenancy;

namespace Trading.Service.Tests.Support;

/// <summary>A second firm next to the development firm, with its own server, group and admin key.</summary>
internal static class SecondFirm
{
    public const string ApiKey = "other-admin-key";

    public const string Server = "other-firm";

    public const string Group = "other";

    public static readonly IReadOnlyDictionary<string, string> Settings = new Dictionary<string, string>
    {
        ["Trading:Groups:1:Id"] = Group,
        ["Trading:Groups:1:Currency"] = "USD",
        ["Trading:Groups:1:StopOutLevelPercent"] = "50",
        ["Trading:Groups:1:Symbols:0:Symbol"] = "EURUSD",
        ["Trading:Groups:1:Symbols:0:Leverage"] = "100",
        ["Trading:Groups:1:Symbols:0:SpreadMarkupPoints"] = "0",
        ["Trading:Groups:1:Symbols:0:CommissionPerLotPerSide"] = "0",
        ["Tenants:1:Id"] = Server,
        ["Tenants:1:Name"] = "Other Firm",
        ["Tenants:1:Groups:0"] = Group,
        ["Tenants:1:AdminApiKeySha256"] = TenantCatalog.HashApiKey(ApiKey),
    };
}
