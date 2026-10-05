namespace Prop.Api.Identity;

/// <summary>
/// How a firm checks its traders' IDs, its KYC (ADR 0042). A firm chooses one before it goes live; ticking "ID checked"
/// by hand is only an exception.
/// </summary>
public enum IdentityMode
{
    /// <summary>Our built-in checks through Didit, an addition to the monthly package.</summary>
    BuiltIn,

    /// <summary>The firm's own service: its page checks the trader, and its systems tell us the outcome through the firm API.</summary>
    External,
}

/// <summary>
/// Whether a firm's KYC is ready for going live: not chosen, its own service not yet tried through the whole flow, or
/// ready. Our review does not wait for it.
/// </summary>
public enum IdentityReadiness
{
    /// <summary>The firm has not chosen how its traders are checked.</summary>
    NotChosen,

    /// <summary>
    /// The firm's own service has not worked through the whole flow since its address was set: a trader started the check
    /// in the portal, and the service reported the outcome through the firm API.
    /// </summary>
    NotTested,

    /// <summary>Our built-in checks, or the firm's own service that has worked through the whole flow.</summary>
    Ready,
}

/// <summary>What waits until the trader's ID is checked.</summary>
public enum IdentityRequirement
{
    /// <summary>The trader cannot ask for a payout in the portal.</summary>
    FirstPayout,

    /// <summary>The firm cannot approve the funded account.</summary>
    Funding,
}

/// <summary>Where a trader's ID check is.</summary>
public enum IdentityStatus
{
    NotStarted,

    /// <summary>Started, and not sent in yet.</summary>
    Pending,

    /// <summary>Sent in, and a person at the provider looks at it.</summary>
    InReview,

    Approved,

    /// <summary>Not approved. The trader can try again.</summary>
    Declined,

    /// <summary>Given up or run out before it was sent in. The trader can start again.</summary>
    Expired,
}

/// <summary>Who checked a trader's ID.</summary>
public enum IdentityProvider
{
    Didit,

    /// <summary>A page in the portal that approves or declines without a real check, for the sandbox and development.</summary>
    Test,

    /// <summary>The firm's own service.</summary>
    External,
}

/// <summary>
/// A firm's choice: how traders are checked, what waits for the check, extra checks with the built-in ones, the firm's
/// own page for <see cref="IdentityMode.External"/>, since when the built-in checks have been on, and when the firm's own
/// service first worked through the whole flow at its address.
/// </summary>
internal sealed record IdentitySettings(
    IdentityMode Mode,
    IdentityRequirement RequiredBefore,
    bool CheckAddress,
    bool CheckSanctions,
    string? ExternalUrl,
    DateTimeOffset? BuiltInSince = null,
    DateTimeOffset? ExternalTestedAt = null)
{
    public IdentityReadiness Readiness => ReadinessOf(Mode, ExternalTestedAt is not null);

    /// <summary>Whether the requirement holds things up.</summary>
    public bool Requires(IdentityRequirement requirement) => RequiredBefore == requirement;

    /// <summary>A firm's readiness, from its choice or null when it has not chosen.</summary>
    public static IdentityReadiness ReadinessOf(IdentitySettings? settings) => settings?.Readiness ?? IdentityReadiness.NotChosen;

    public static IdentityReadiness ReadinessOf(IdentityMode? mode, bool externalTested) => mode switch
    {
        null => IdentityReadiness.NotChosen,
        IdentityMode.External when !externalTested => IdentityReadiness.NotTested,
        _ => IdentityReadiness.Ready,
    };
}

/// <summary>A trader's ID check as it stands, and what the document said when it was approved.</summary>
internal sealed record TraderIdentity(
    Guid TraderId,
    IdentityProvider Provider,
    Guid? SessionId,
    IdentityStatus Status,
    string? FullName,
    DateOnly? DateOfBirth,
    string? Country,
    bool AddressChecked,
    bool SanctionsChecked,
    string? Reason,
    DateTimeOffset? DecidedAt,
    DateTimeOffset UpdatedAt);

/// <summary>A check started with a provider, and the page where the trader does it.</summary>
internal sealed record IdentitySession(
    Guid Id,
    string FirmId,
    Guid TraderId,
    IdentityProvider Provider,
    string ProviderSessionId,
    Uri Url,
    bool CheckAddress,
    bool CheckSanctions,
    IdentityStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CheckedAt);

/// <summary>
/// What a provider decided about a check. The name, date of birth and country are from the document, and
/// <paramref name="Reason"/> is why it was declined.
/// </summary>
internal sealed record IdentityDecision(
    IdentityStatus Status,
    string? FullName,
    DateOnly? DateOfBirth,
    string? Country,
    bool AddressApproved,
    bool SanctionsClear,
    string? Reason);

/// <summary>What a firm reports about a trader checked by its own service.</summary>
public sealed record ExternalIdentityRequest(
    string? Email,
    IdentityStatus? Status,
    string? Reason,
    string? FullName,
    DateOnly? DateOfBirth,
    string? Country,
    bool AddressChecked = false);

/// <summary>A firm's choice of how its traders are checked, as the admin panel saves it. <paramref name="Mode"/> must be chosen.</summary>
public sealed record IdentitySettingsRequest(IdentityMode? Mode, IdentityRequirement RequiredBefore, bool CheckAddress, bool CheckSanctions, string? ExternalUrl);

/// <summary>What the built-in checks cost the firm, from our prices.</summary>
public sealed record IdentityPricesResponse(string Currency, decimal MonthlyPrice, int Included, decimal PerCheck, decimal Address, decimal Sanctions);

/// <summary>
/// The firm's choice, our prices for the built-in checks, and the built-in checks its traders sent in since the firm was
/// last charged for them. <paramref name="Mode"/> is null until the firm chooses, and <paramref name="Readiness"/> says
/// whether the firm can go live, with <paramref name="ExternalTestedAt"/> when its own service first worked
/// through the whole flow. In the sandbox the checks are test checks, which cost nothing.
/// </summary>
public sealed record IdentitySettingsResponse(
    IdentityMode? Mode,
    IdentityRequirement RequiredBefore,
    bool CheckAddress,
    bool CheckSanctions,
    string? ExternalUrl,
    IdentityPricesResponse Prices,
    int ChecksSinceLastCharge,
    bool TestChecks,
    IdentityReadiness Readiness,
    DateTimeOffset? ExternalTestedAt)
{
    internal static IdentitySettingsResponse From(IdentitySettings? settings, IdentityPricesResponse prices, int checks, bool testChecks) =>
        settings is null
            ? new(null, IdentityRequirement.FirstPayout, false, false, null, prices, checks, testChecks, IdentityReadiness.NotChosen, null)
            : new(
                settings.Mode,
                settings.RequiredBefore,
                settings.CheckAddress,
                settings.CheckSanctions,
                settings.ExternalUrl,
                prices,
                checks,
                testChecks,
                settings.Readiness,
                settings.ExternalTestedAt);
}

/// <summary>
/// A trader's ID check as the firm sees it: where it is, who checked, what the document said, why it was declined, and
/// when it was decided.
/// </summary>
public sealed record TraderIdentityResponse(
    IdentityStatus Status,
    IdentityProvider Provider,
    string? FullName,
    DateOnly? DateOfBirth,
    string? Country,
    bool AddressChecked,
    bool SanctionsChecked,
    string? Reason,
    DateTimeOffset? DecidedAt)
{
    internal static TraderIdentityResponse? From(TraderIdentity? identity) =>
        identity is null
            ? null
            : new(identity.Status, identity.Provider, identity.FullName, identity.DateOfBirth, identity.Country, identity.AddressChecked, identity.SanctionsChecked, identity.Reason, identity.DecidedAt);
}

/// <summary>
/// The trader's own ID check: how the firm checks, what waits for it, where the check is, and why it was declined.
/// <paramref name="Mode"/> is null while the firm has not chosen, and then nothing waits for a check.
/// <paramref name="Verified"/> also holds when the firm ticked "ID checked" by hand, as an exception. <paramref name="CanStart"/>
/// is whether the trader can start a check now.
/// </summary>
public sealed record MyIdentityResponse(
    IdentityMode? Mode,
    IdentityRequirement RequiredBefore,
    IdentityStatus Status,
    bool Verified,
    bool CanStart,
    string? Reason,
    DateTimeOffset? DecidedAt);

/// <summary>Where the trader does the check: the provider's page, or the firm's own.</summary>
public sealed record IdentityStartResponse(Uri Url);

/// <summary>A test check's outcome, chosen on the test page.</summary>
public sealed record TestIdentityRequest(bool Approve);

/// <summary>What a change to ID checks came to: done, or refused with a status code, the reason and the field it is about.</summary>
internal abstract record IdentityResult
{
    public sealed record Done : IdentityResult;

    public sealed record Refused(int StatusCode, string Problem, string? Field = null) : IdentityResult;
}
