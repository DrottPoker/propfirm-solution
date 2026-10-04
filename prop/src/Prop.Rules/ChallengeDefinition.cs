using System.Text.RegularExpressions;

namespace Prop.Rules;

/// <summary>Where the daily loss limit starts from when a trading day starts.</summary>
public enum DailyLossReference
{
    /// <summary>The balance at the start of the day. Open losses carried over count against the new day.</summary>
    Balance,

    /// <summary>The higher of balance and equity at the start of the day.</summary>
    HigherOfBalanceAndEquity,
}

/// <summary>Equity may not fall more than <paramref name="Percent"/> of the initial balance below the day's starting point.</summary>
public sealed record DailyLossRule(decimal Percent, DailyLossReference Reference);

public enum MaxLossKind
{
    /// <summary>A fixed level below the initial balance.</summary>
    Fixed,

    /// <summary>Follows the highest equity at the same distance and stops rising at the initial balance.</summary>
    Trailing,
}

/// <summary>Equity may not fall more than <paramref name="Percent"/> of the initial balance below the initial balance, or below the highest equity when trailing.</summary>
public sealed record MaxLossRule(decimal Percent, MaxLossKind Kind);

/// <summary>
/// Rules for one stage of a challenge. An evaluation stage is passed when the balance reaches the profit
/// target with no open positions after at least <paramref name="MinTradingDays"/> trading days, and within
/// <paramref name="MaxDays"/> days after the day it started when it has a time limit.
/// The funded stage has no profit target and no time limit. Its trader gets <paramref name="ProfitSplitPercent"/>
/// of the profit as a payout, after at least <paramref name="MinTradingDays"/> trading days since the last one. With
/// <paramref name="ConsistencyPercent"/>, the best trading day since the last payout may have made at most that share
/// of the profit, so a payout is not one lucky day.
/// </summary>
public sealed record StageRules(
    string Name,
    decimal? ProfitTargetPercent,
    int MinTradingDays,
    DailyLossRule DailyLoss,
    MaxLossRule MaxLoss,
    decimal? ProfitSplitPercent = null,
    int? MaxDays = null,
    decimal? ConsistencyPercent = null);

/// <summary>When a trading day starts, as a time of day in an IANA time zone. The service turns it into trading days.</summary>
public sealed record TradingDayDefinition(string TimeZone, TimeOnly Start);

/// <summary>
/// A challenge a firm sells: the account size, the trading day and the rules of each stage. Traders go
/// through the evaluation stages in order and then trade a funded account. Without evaluation stages the trader is
/// funded from the start, which firms sell as instant funding. Every challenge keeps the
/// definition it was bought with, so later changes by the firm never affect it. With
/// <paramref name="InactivityDays"/>, a challenge ends when no position was opened for that many days, in
/// every stage including the funded one.
/// </summary>
public sealed partial record ChallengeDefinition(
    string Id,
    string Name,
    string Currency,
    decimal InitialBalance,
    TradingDayDefinition TradingDay,
    IReadOnlyList<StageRules> Evaluation,
    StageRules Funded,
    int? InactivityDays = null)
{
    /// <summary>The longest inactivity and time limit, in days.</summary>
    public const int MaxDayLimit = 365;

    /// <summary>The index of the funded stage, after the evaluation stages.</summary>
    public int FundedStage => Evaluation.Count;

    public StageRules Stage(int index) => index < Evaluation.Count ? Evaluation[index] : Funded;

    /// <summary>A percentage of the initial balance, rounded to whole cents.</summary>
    public decimal PercentOfInitialBalance(decimal percent) =>
        decimal.Round(InitialBalance * percent / 100m, 2, MidpointRounding.AwayFromZero);

    /// <summary>What is wrong with the definition. Empty when it is valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        Require(Id.Trim().Length > 0, "The challenge needs an id.");
        Require(Name.Trim().Length > 0, "The challenge needs a name.");
        Require(CurrencyCode().IsMatch(Currency), "The currency must be three capital letters, for example USD.");
        Require(InitialBalance > 0 && decimal.Round(InitialBalance, 2) == InitialBalance, "The initial balance must be positive, in whole cents.");
        Require(TradingDay.TimeZone.Trim().Length > 0, "The trading day needs a time zone.");
        Require(
            InactivityDays is null or (>= 1 and <= MaxDayLimit),
            $"Inactivity must be 1 to {MaxDayLimit} days without a new position, or empty for no rule.");

        foreach (var (stage, index) in Evaluation.Select((s, i) => (s, i + 1)))
        {
            Require(stage.ProfitTargetPercent is > 0 and <= 100, $"Evaluation stage {index} needs a profit target above 0 and at most 100 percent.");
            Require(stage.ProfitSplitPercent is null, $"Evaluation stage {index} has no profit split. Only the funded stage pays out.");
            Require(stage.ConsistencyPercent is null, $"Evaluation stage {index} has no consistency rule. It is a rule for payouts.");
            Require(
                stage.MaxDays is null or (>= 1 and <= MaxDayLimit),
                $"Evaluation stage {index} needs a time limit of 1 to {MaxDayLimit} days, or empty for none.");
            Require(
                stage.MaxDays is not { } maxDays || maxDays >= stage.MinTradingDays,
                $"Evaluation stage {index} needs a time limit of at least its minimum trading days.");
            ValidateStage(stage, $"Evaluation stage {index}");
        }

        Require(Funded.ProfitTargetPercent is null, "The funded stage has no profit target.");
        Require(Funded.ProfitSplitPercent is > 0 and <= 100, "The funded stage needs a profit split above 0 and at most 100 percent.");
        Require(Funded.MaxDays is null, "The funded stage has no time limit.");
        Require(
            Funded.ConsistencyPercent is not { } consistency || (consistency is >= 10 and <= 100 && decimal.Round(consistency, 2) == consistency),
            "The consistency rule must be 10 to 100 percent, with at most two decimals, or empty for none.");
        ValidateStage(Funded, "The funded stage");
        return errors;

        void ValidateStage(StageRules stage, string name)
        {
            Require(stage.Name.Trim().Length > 0, $"{name} needs a name.");
            Require(stage.MinTradingDays >= 0, $"{name} cannot require a negative number of trading days.");
            Require(stage.DailyLoss.Percent is > 0 and < 100, $"{name} needs a daily loss above 0 and below 100 percent.");
            Require(stage.MaxLoss.Percent is > 0 and < 100, $"{name} needs a max loss above 0 and below 100 percent.");
            Require(stage.DailyLoss.Percent <= stage.MaxLoss.Percent, $"{name} cannot allow a larger daily loss than max loss.");
        }

        void Require(bool condition, string error)
        {
            if (!condition)
            {
                errors.Add(error);
            }
        }
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyCode();
}

/// <summary>Ready-made challenges that firms start from.</summary>
public static class ChallengeTemplates
{
    /// <summary>Days without a new position before a challenge from a template ends.</summary>
    public const int DefaultInactivityDays = 30;

    /// <summary>An account size as traders say it: 100K for 100,000, 12.5K for 12,500 and 1M for 1,000,000.</summary>
    public static string SizeName(decimal balance) =>
        balance >= 1_000_000m && balance % 100_000m == 0 ? FormattableString.Invariant($"{balance / 1_000_000m:0.#}M")
        : balance >= 1_000m && balance % 100m == 0 ? FormattableString.Invariant($"{balance / 1_000m:0.#}K")
        : FormattableString.Invariant($"{balance:#,0.##}");

    /// <summary>
    /// A one-step challenge: a profit target of 10 percent, 4 percent daily loss from the balance at the start of the
    /// day, 6 percent fixed max loss and at least 4 trading days. Funded as in <see cref="TwoStep"/>.
    /// </summary>
    public static ChallengeDefinition OneStep(string id, decimal initialBalance, string currency = "USD")
    {
        var dailyLoss = new DailyLossRule(4, DailyLossReference.Balance);
        var maxLoss = new MaxLossRule(6, MaxLossKind.Fixed);
        return Challenge(id, $"One-step {SizeName(initialBalance)}", currency, initialBalance, [new StageRules("Phase 1", 10, 4, dailyLoss, maxLoss)], dailyLoss, maxLoss, 80);
    }

    /// <summary>
    /// A three-step challenge with smaller steps: profit targets of 6 percent in each phase, 5 percent daily loss, 10
    /// percent fixed max loss and at least 3 trading days per phase. Funded as in <see cref="TwoStep"/>.
    /// </summary>
    public static ChallengeDefinition ThreeStep(string id, decimal initialBalance, string currency = "USD")
    {
        var dailyLoss = new DailyLossRule(5, DailyLossReference.Balance);
        var maxLoss = new MaxLossRule(10, MaxLossKind.Fixed);
        return Challenge(
            id,
            $"Three-step {SizeName(initialBalance)}",
            currency,
            initialBalance,
            [new StageRules("Phase 1", 6, 3, dailyLoss, maxLoss), new StageRules("Phase 2", 6, 3, dailyLoss, maxLoss), new StageRules("Phase 3", 6, 3, dailyLoss, maxLoss)],
            dailyLoss,
            maxLoss,
            80);
    }

    /// <summary>
    /// Instant funding: no evaluation, the trader is funded from the start, with 3 percent daily loss, a 6 percent max
    /// loss that trails the highest equity and a 70 percent profit split, with at least 5 trading days between payouts.
    /// </summary>
    public static ChallengeDefinition InstantFunded(string id, decimal initialBalance, string currency = "USD")
    {
        var dailyLoss = new DailyLossRule(3, DailyLossReference.Balance);
        var maxLoss = new MaxLossRule(6, MaxLossKind.Trailing);
        return Challenge(id, $"Instant funded {SizeName(initialBalance)}", currency, initialBalance, [], dailyLoss, maxLoss, 70);
    }

    private static ChallengeDefinition Challenge(
        string id,
        string name,
        string currency,
        decimal initialBalance,
        IReadOnlyList<StageRules> evaluation,
        DailyLossRule dailyLoss,
        MaxLossRule maxLoss,
        decimal profitSplitPercent) =>
        new(
            id,
            name,
            currency,
            initialBalance,
            new TradingDayDefinition("Europe/Stockholm", TimeOnly.MinValue),
            evaluation,
            new StageRules("Funded", null, 5, dailyLoss, maxLoss, ProfitSplitPercent: profitSplitPercent),
            DefaultInactivityDays);

    /// <summary>
    /// The common two-step challenge: profit targets of 10 and 5 percent, 5 percent daily loss from the
    /// balance at the start of the day, 10 percent fixed max loss and at least 4 trading days per
    /// evaluation stage, with no time limit. The funded trader gets 80 percent of the profit, with at least
    /// 5 trading days between payouts. The challenge ends after 30 days without a new position. Trading days
    /// start at midnight Swedish time.
    /// </summary>
    public static ChallengeDefinition TwoStep(string id, decimal initialBalance, string currency = "USD")
    {
        var dailyLoss = new DailyLossRule(5, DailyLossReference.Balance);
        var maxLoss = new MaxLossRule(10, MaxLossKind.Fixed);
        return new ChallengeDefinition(
            id,
            $"Two-step {SizeName(initialBalance)}",
            currency,
            initialBalance,
            new TradingDayDefinition("Europe/Stockholm", TimeOnly.MinValue),
            [
                new StageRules("Phase 1", 10, 4, dailyLoss, maxLoss),
                new StageRules("Phase 2", 5, 4, dailyLoss, maxLoss),
            ],
            new StageRules("Funded", null, 5, dailyLoss, maxLoss, ProfitSplitPercent: 80),
            DefaultInactivityDays);
    }
}
