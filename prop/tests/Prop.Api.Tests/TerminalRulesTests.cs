using System.Collections.Immutable;

using Prop.Api.Challenges;
using Prop.Api.Trading;
using Prop.Rules;

namespace Prop.Api.Tests;

/// <summary>The challenge's rules as the terminal shows them and warns about them (ADR 0052).</summary>
public sealed class TerminalRulesTests
{
    private static readonly DateOnly Monday = new(2026, 10, 5);

    // Deadlines are the starts of their trading days, midnight in Stockholm.
    [Fact]
    public void AnEvaluationStageHasItsTradingDaysAndDeadlines()
    {
        var state = State(ChallengeTemplates.TwoStep("two-step", 100_000m), stage: 0, ChallengeStatus.Active) with
        {
            StageDeadline = Monday.AddDays(14),
            InactivityDeadline = Monday.AddDays(31),
        };

        var rules = TerminalRules.Of(state);

        Assert.Equal(
            new TradingAccountRules(false, 4, 2, new DateTimeOffset(2026, 10, 18, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 4, 23, 0, 0, TimeSpan.Zero), null, null),
            rules);
    }

    [Fact]
    public void AFundedAccountShowsTheBestDaysShareOfTheProfit()
    {
        var template = ChallengeTemplates.TwoStep("consistent", 100_000m);
        var definition = template with { Funded = template.Funded with { ConsistencyPercent = 40m } };
        var state = State(definition, definition.FundedStage, ChallengeStatus.Active) with
        {
            Account = new AccountFigures(110_000m, 0),
            DayProfits = ImmutableSortedDictionary.CreateRange(new Dictionary<DateOnly, decimal>
            {
                [Monday] = 6_000m,
                [Monday.AddDays(1)] = 1_500m,
                [Monday.AddDays(2)] = 2_500m,
            }),
        };

        var rules = TerminalRules.Of(state)!;

        Assert.Equal((true, (int?)5, 2), (rules.Funded, rules.TradingDaysRequired, rules.TradingDaysCounted));
        Assert.Equal(((decimal?)40m, (decimal?)60m), (rules.ConsistencyPercent, rules.BestDayPercent));
    }

    // Waiting for funding nothing runs out, and an ended challenge has nothing left to show.
    [Fact]
    public void DeadlinesAreOnlyWhileTheStageIsTraded()
    {
        var definition = ChallengeTemplates.TwoStep("two-step", 100_000m);
        var waiting = State(definition, stage: 1, ChallengeStatus.AwaitingFunding) with { StageDeadline = Monday, InactivityDeadline = Monday };

        var rules = TerminalRules.Of(waiting)!;

        Assert.Equal(((DateTimeOffset?)null, (DateTimeOffset?)null), (rules.PassBy, rules.OpenPositionBy));
        Assert.Null(TerminalRules.Of(waiting with { Status = ChallengeStatus.Failed }));
        Assert.Null(TerminalRules.Of(waiting with { AccountId = null, Status = ChallengeStatus.OpeningAccount }));
    }

    private static ChallengeState State(ChallengeDefinition definition, int stage, ChallengeStatus status) =>
        new(
            "C1",
            definition,
            stage,
            status,
            "A1",
            Monday.AddDays(2),
            [Monday, Monday.AddDays(1)],
            new AccountFigures(100_000m, 0),
            LastSequence: 1);
}
