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
/// target with no open positions after at least <paramref name="MinTradingDays"/> trading days.
/// The funded stage has no profit target.
/// </summary>
public sealed record StageRules(
    string Name,
    decimal? ProfitTargetPercent,
    int MinTradingDays,
    DailyLossRule DailyLoss,
    MaxLossRule MaxLoss);

/// <summary>When a trading day starts, as a time of day in an IANA time zone. The service turns it into trading days.</summary>
public sealed record TradingDayDefinition(string TimeZone, TimeOnly Start);

/// <summary>
/// A challenge a firm sells: the account size, the trading day and the rules of each stage. Traders go
/// through the evaluation stages in order and then trade a funded account. Every challenge keeps the
/// definition it was bought with, so later changes by the firm never affect it.
/// </summary>
public sealed partial record ChallengeDefinition(
    string Id,
    string Name,
    string Currency,
    decimal InitialBalance,
    TradingDayDefinition TradingDay,
    IReadOnlyList<StageRules> Evaluation,
    StageRules Funded)
{
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
        Require(Evaluation.Count > 0, "The challenge needs at least one evaluation stage.");

        foreach (var (stage, index) in Evaluation.Select((s, i) => (s, i + 1)))
        {
            Require(stage.ProfitTargetPercent is > 0 and <= 100, $"Evaluation stage {index} needs a profit target above 0 and at most 100 percent.");
            ValidateStage(stage, $"Evaluation stage {index}");
        }

        Require(Funded.ProfitTargetPercent is null, "The funded stage has no profit target.");
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
    /// <summary>
    /// The common two-step challenge: profit targets of 10 and 5 percent, 5 percent daily loss from the
    /// balance at the start of the day, 10 percent fixed max loss and at least 4 trading days per
    /// evaluation stage. Trading days start at midnight Swedish time.
    /// </summary>
    public static ChallengeDefinition TwoStep(string id, decimal initialBalance, string currency = "USD")
    {
        var dailyLoss = new DailyLossRule(5, DailyLossReference.Balance);
        var maxLoss = new MaxLossRule(10, MaxLossKind.Fixed);
        return new ChallengeDefinition(
            id,
            FormattableString.Invariant($"Two-step {initialBalance:0.##} {currency}"),
            currency,
            initialBalance,
            new TradingDayDefinition("Europe/Stockholm", TimeOnly.MinValue),
            [
                new StageRules("Phase 1", 10, 4, dailyLoss, maxLoss),
                new StageRules("Phase 2", 5, 4, dailyLoss, maxLoss),
            ],
            new StageRules("Funded", null, 0, dailyLoss, maxLoss));
    }
}
