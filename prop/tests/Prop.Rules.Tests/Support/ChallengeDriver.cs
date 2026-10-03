namespace Prop.Rules.Tests.Support;

/// <summary>Runs one challenge through the rule engine, like the service will, with a clock and sequence numbers that only move forward.</summary>
internal sealed class ChallengeDriver
{
    public static readonly DateOnly Monday = new(2026, 10, 5);

    private DateTimeOffset _time = new(2026, 10, 5, 6, 0, 0, TimeSpan.Zero);
    private long _sequence;

    public ChallengeDriver(ChallengeDefinition? definition = null)
    {
        var step = ChallengeRules.Start("C1", definition ?? ChallengeTemplates.TwoStep("two-step-100k", 100_000m), _time);
        State = step.State;
        Outputs.AddRange(step.Outputs);
    }

    public ChallengeState State { get; private set; }

    /// <summary>Every output so far, in order.</summary>
    public List<ChallengeOutput> Outputs { get; } = [];

    public DateTimeOffset NextTime() => _time = _time.AddMinutes(1);

    public long NextSequence() => ++_sequence;

    public IReadOnlyList<ChallengeOutput> Apply(ChallengeInput input)
    {
        var step = ChallengeRules.Apply(State, input);
        State = step.State;
        Outputs.AddRange(step.Outputs);
        return step.Outputs;
    }

    public IReadOnlyList<ChallengeOutput> OpenAccount(string accountId, DateOnly day) =>
        Apply(new AccountOpened(NextTime(), accountId, NextSequence(), day));

    public IReadOnlyList<ChallengeOutput> StartDay(DateOnly day) => Apply(new TradingDayStarted(NextTime(), day));

    public IReadOnlyList<ChallengeOutput> OpenPosition(DateOnly day) =>
        Apply(new PositionOpened(NextTime(), State.AccountId!, NextSequence(), day));

    public IReadOnlyList<ChallengeOutput> Update(decimal balance, decimal equity, int openPositions = 0) =>
        Apply(new AccountUpdated(NextTime(), State.AccountId!, NextSequence(), balance, equity, openPositions));

    public IReadOnlyList<ChallengeOutput> Breach(string floorId, decimal level, decimal equity) =>
        Apply(new FloorBreached(NextTime(), State.AccountId!, NextSequence(), floorId, level, equity));

    /// <summary>Trades on <paramref name="days"/> trading days from <paramref name="firstDay"/>.</summary>
    public void TradeOnDays(DateOnly firstDay, int days)
    {
        for (var day = firstDay; day < firstDay.AddDays(days); day = day.AddDays(1))
        {
            StartDay(day);
            OpenPosition(day);
        }
    }

    /// <summary>Opens the stage's account and passes the stage with the given balance on four trading days.</summary>
    public IReadOnlyList<ChallengeOutput> PassStage(string accountId, DateOnly firstDay, decimal balance)
    {
        OpenAccount(accountId, firstDay);
        TradeOnDays(firstDay, 4);
        return Update(balance, balance);
    }

    /// <summary>The outputs without their times, to compare with expected outputs created with a default time.</summary>
    public static ChallengeOutput[] WithoutTime(IEnumerable<ChallengeOutput> outputs) => [.. outputs.Select(o => o with { Time = default })];
}
