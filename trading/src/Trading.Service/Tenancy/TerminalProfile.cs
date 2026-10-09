namespace Trading.Service.Tenancy;

/// <summary>
/// What kind of business runs the server, which decides the terminal's words and which parts it shows to begin with
/// (ADR 0058): a prop firm with challenges and rules, a broker with clients' own money, a school or practice platform,
/// or a trading desk of its own.
/// </summary>
public enum TerminalKind
{
    Prop,
    Broker,
    Practice,
    Desk,
}

/// <summary>Who sets a server's kind of business: our staff, the partner that made it, or the service's configuration.</summary>
public enum TerminalSetBy
{
    Staff,
    Partner,
    Configuration,
}

/// <summary>How an order ticket starts on a symbol the trader has not traded before.</summary>
public enum StartingSizeKind
{
    /// <summary>The smallest volume the instrument allows.</summary>
    Smallest,

    /// <summary>A volume in lots, lowered or raised to what the instrument allows.</summary>
    Lots,

    /// <summary>Sized from a share of the room left to the nearest loss limit, at the stop loss the trader sets.</summary>
    RiskOfRoom,
}

/// <summary>The parts of the terminal that are shown. Each can be turned off for a server that has no use for it.</summary>
/// <param name="Rulebook">The account's rules and limits behind the Rules button, and the warnings about them.</param>
/// <param name="OwnLimits">The trader's own daily limits and the lock for the rest of the day (ADR 0054).</param>
/// <param name="RiskSizing">Sizing an order from what it risks at its stop loss (ADR 0052).</param>
/// <param name="TradeDetails">Each closed trade's details with the feed's prices behind its fills (ADR 0053).</param>
/// <param name="BreachReports">The report of a broken loss limit (ADR 0053).</param>
public sealed record TerminalModules(bool Rulebook, bool OwnLimits, bool RiskSizing, bool TradeDetails, bool BreachReports);

/// <summary>How an order ticket starts on a symbol. <paramref name="Value"/> is the lots or the percent of the room, and null for the smallest.</summary>
public sealed record StartingSize(StartingSizeKind Kind, decimal? Value);

/// <summary>The firm's own pages the terminal links to, each null when the firm has none.</summary>
public sealed record TerminalLinks(Uri? Help, Uri? Support, Uri? Terms, Uri? Privacy, Uri? PasswordReset);

/// <summary>
/// How a server's terminal works for its traders (ADR 0058): the kind of business, the parts shown, whether an order
/// asks before it is sent and how a ticket starts, whether traders can log in to the terminal with a password, the
/// firm's own pages, and a warning about risk shown at the login, such as a broker must show. Every server has one;
/// one not set is the default for its kind. The terminal's look is always ours (ADR 0009).
/// </summary>
public sealed record TerminalProfile(
    TerminalKind Kind,
    TerminalModules Modules,
    bool ConfirmOrders,
    StartingSize StartingSize,
    bool PasswordLogin,
    TerminalLinks Links,
    string? RiskWarning)
{
    public const int MaxRiskWarningLength = 600;

    public static readonly TerminalLinks NoLinks = new(null, null, null, null, null);

    /// <summary>
    /// The profile a server has until its firm sets one: a prop firm whose traders may log in with a password. A firm that
    /// logs its traders in through its own portal, as Kronant Prop does, turns password login off in its profile.
    /// </summary>
    public static readonly TerminalProfile Standard = Default(TerminalKind.Prop, passwordLogin: true);

    /// <summary>
    /// The profile for another kind of business: the kind's words, the parts it shows and whether its orders ask first.
    /// The starting size, password login, the firm's pages and the risk warning stay as they are.
    /// </summary>
    public TerminalProfile WithKind(TerminalKind kind)
    {
        var standard = Default(kind, PasswordLogin);
        return this with { Kind = kind, Modules = standard.Modules, ConfirmOrders = standard.ConfirmOrders };
    }

    /// <summary>What each kind of business starts with.</summary>
    public static TerminalProfile Default(TerminalKind kind, bool passwordLogin) => kind switch
    {
        TerminalKind.Prop => new(kind, new TerminalModules(true, true, true, true, true), false, new StartingSize(StartingSizeKind.Smallest, null), passwordLogin, NoLinks, null),
        // A client's own money: every order asks first, and nothing speaks of challenges or broken limits.
        TerminalKind.Broker => new(kind, new TerminalModules(false, true, true, true, false), true, new StartingSize(StartingSizeKind.Smallest, null), passwordLogin, NoLinks, null),
        TerminalKind.Practice => new(kind, new TerminalModules(false, false, true, true, false), false, new StartingSize(StartingSizeKind.Smallest, null), passwordLogin, NoLinks, null),
        TerminalKind.Desk => new(kind, new TerminalModules(true, false, true, true, true), false, new StartingSize(StartingSizeKind.Smallest, null), passwordLogin, NoLinks, null),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Why the profile cannot be used, or null when it can.</summary>
    public string? Problem()
    {
        if (!Enum.IsDefined(Kind))
        {
            return "Kind must be Prop, Broker, Practice or Desk.";
        }

        var problem = StartingSize switch
        {
            { Kind: StartingSizeKind.Smallest, Value: not null } => "A starting size of the smallest volume takes no value.",
            { Kind: StartingSizeKind.Lots, Value: not (> 0m and <= 1_000m) } => "A starting size in lots needs a value above 0, at most 1000.",
            { Kind: StartingSizeKind.RiskOfRoom, Value: not (>= 0.01m and <= 100m) } => "A starting size from the room needs a percent from 0.01 to 100.",
            _ when !Enum.IsDefined(StartingSize.Kind) => "The starting size must be Smallest, Lots or RiskOfRoom.",
            _ => null,
        };
        if (problem is not null)
        {
            return problem;
        }

        if (StartingSize.Kind == StartingSizeKind.RiskOfRoom && !Modules.RiskSizing)
        {
            return "A starting size from the room needs risk sizing to be shown.";
        }

        Uri?[] links = [Links.Help, Links.Support, Links.Terms, Links.Privacy, Links.PasswordReset];
        if (links.Any(l => l is not null && !TenantCatalog.IsValidLoginUrl(l)))
        {
            return "Every link must be an absolute http or https address.";
        }

        return RiskWarning is { } warning && (warning.Trim().Length == 0 || warning.Length > MaxRiskWarningLength)
            ? $"The risk warning must have text, at most {MaxRiskWarningLength} characters, or be left out."
            : null;
    }
}
