using Trading.Engine;

namespace Trading.Service.Configuration;

/// <summary>Instruments, groups and development accounts from the "Trading" configuration section.</summary>
public sealed class TradingOptions
{
    public const string SectionName = "Trading";

    public IReadOnlyList<InstrumentOptions> Instruments { get; init; } = [];

    public IReadOnlyList<GroupOptions> Groups { get; init; } = [];

    public TimeSpan MaxQuoteAge { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>Accounts created at startup. For local development.</summary>
    public IReadOnlyList<SeedAccountOptions> SeedAccounts { get; init; } = [];

    public EngineConfiguration ToEngineConfiguration() =>
        new(
            Instruments
                .Select(i => new Instrument(i.Symbol, i.BaseCurrency, i.QuoteCurrency, i.ContractSize, i.Digits, i.VolumeMin, i.VolumeStep, i.VolumeMax))
                .ToList(),
            Groups
                .Select(g => new TradingGroup(
                    g.Id,
                    g.Currency,
                    g.StopOutLevelPercent,
                    g.Symbols.Select(s => new SymbolConditions(s.Symbol, s.Leverage, s.SpreadMarkupPoints, s.CommissionPerLotPerSide)).ToList()))
                .ToList(),
            MaxQuoteAge);
}

public sealed class InstrumentOptions
{
    public string Symbol { get; init; } = "";

    public string BaseCurrency { get; init; } = "";

    public string QuoteCurrency { get; init; } = "";

    public decimal ContractSize { get; init; }

    public int Digits { get; init; }

    public decimal VolumeMin { get; init; }

    public decimal VolumeStep { get; init; }

    public decimal VolumeMax { get; init; }
}

public sealed class GroupOptions
{
    public string Id { get; init; } = "";

    public string Currency { get; init; } = "";

    public decimal StopOutLevelPercent { get; init; }

    public IReadOnlyList<SymbolConditionsOptions> Symbols { get; init; } = [];
}

public sealed class SymbolConditionsOptions
{
    public string Symbol { get; init; } = "";

    public int Leverage { get; init; }

    public int SpreadMarkupPoints { get; init; }

    public decimal CommissionPerLotPerSide { get; init; }
}

public sealed class SeedAccountOptions
{
    public string AccountId { get; init; } = "";

    public string GroupId { get; init; } = "";

    public decimal InitialBalance { get; init; }

    public IReadOnlyList<SeedFloorOptions> Floors { get; init; } = [];
}

/// <summary>A fixed floor when only Level is set, a trailing floor when TrailingDistance is set.</summary>
public sealed class SeedFloorOptions
{
    public string FloorId { get; init; } = "";

    public decimal? Level { get; init; }

    public decimal? TrailingDistance { get; init; }

    public decimal? LockLevel { get; init; }

    public EquityFloorRule ToRule() =>
        TrailingDistance is { } distance ? new TrailingFloor(distance, LockLevel)
        : Level is { } level ? new FixedFloor(level)
        : throw new InvalidOperationException($"Seed floor {FloorId} needs Level or TrailingDistance.");
}
