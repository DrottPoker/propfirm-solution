namespace Prop.Rules.Tests;

public sealed class ChallengeDefinitionTests
{
    private static readonly ChallengeDefinition TwoStep = ChallengeTemplates.TwoStep("two-step-100k", 100_000m);

    [Fact]
    public void TwoStepTemplateIsValid()
    {
        Assert.Empty(TwoStep.Validate());
        Assert.Equal([10m, 5m], TwoStep.Evaluation.Select(s => s.ProfitTargetPercent!.Value));
        Assert.Equal(2, TwoStep.FundedStage);
        Assert.Same(TwoStep.Funded, TwoStep.Stage(TwoStep.FundedStage));
        Assert.Equal(new TradingDayDefinition("Europe/Stockholm", TimeOnly.MinValue), TwoStep.TradingDay);
        Assert.Equal((80m, 5), (TwoStep.Funded.ProfitSplitPercent, TwoStep.Funded.MinTradingDays));
    }

    [Theory]
    [InlineData("no evaluation stage", "at least one evaluation stage")]
    [InlineData("target on funded stage", "funded stage has no profit target")]
    [InlineData("no target on evaluation stage", "needs a profit target")]
    [InlineData("daily loss above max loss", "larger daily loss than max loss")]
    [InlineData("negative trading days", "negative number of trading days")]
    [InlineData("lowercase currency", "three capital letters")]
    [InlineData("fractions of cents", "in whole cents")]
    [InlineData("no time zone", "needs a time zone")]
    [InlineData("no profit split", "needs a profit split")]
    [InlineData("profit split above 100", "needs a profit split")]
    [InlineData("profit split on evaluation stage", "has no profit split")]
    public void InvalidDefinitionsAreRefused(string problem, string error)
    {
        var phase1 = TwoStep.Evaluation[0];
        var definition = problem switch
        {
            "no evaluation stage" => TwoStep with { Evaluation = [] },
            "target on funded stage" => TwoStep with { Funded = TwoStep.Funded with { ProfitTargetPercent = 5 } },
            "no target on evaluation stage" => TwoStep with { Evaluation = [phase1 with { ProfitTargetPercent = null }] },
            "daily loss above max loss" => TwoStep with { Evaluation = [phase1 with { DailyLoss = phase1.DailyLoss with { Percent = 12 } }] },
            "negative trading days" => TwoStep with { Evaluation = [phase1 with { MinTradingDays = -1 }] },
            "lowercase currency" => TwoStep with { Currency = "usd" },
            "fractions of cents" => TwoStep with { InitialBalance = 100_000.001m },
            "no time zone" => TwoStep with { TradingDay = TwoStep.TradingDay with { TimeZone = " " } },
            "no profit split" => TwoStep with { Funded = TwoStep.Funded with { ProfitSplitPercent = null } },
            "profit split above 100" => TwoStep with { Funded = TwoStep.Funded with { ProfitSplitPercent = 100.5m } },
            "profit split on evaluation stage" => TwoStep with { Evaluation = [phase1 with { ProfitSplitPercent = 80 }] },
            _ => throw new ArgumentOutOfRangeException(nameof(problem)),
        };

        Assert.Contains(definition.Validate(), e => e.Contains(error, StringComparison.Ordinal));
        Assert.Throws<ArgumentException>(() => ChallengeRules.Start("C1", definition, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void PercentagesOfTheInitialBalanceAreRoundedToWholeCents()
    {
        var odd = TwoStep with { InitialBalance = 33_333.33m };

        Assert.Equal(1_666.67m, odd.PercentOfInitialBalance(5));
        Assert.Equal(10_000m, TwoStep.PercentOfInitialBalance(10));
    }
}
