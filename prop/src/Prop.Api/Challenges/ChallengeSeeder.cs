using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Api.Payments;
using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>
/// Creates or replaces each firm's configured challenges at startup, so they follow their template, with the
/// configured price in the portal. Accounts already started keep the definition they were started with.
/// </summary>
internal sealed class ChallengeSeeder(FirmCatalog firms, ChallengeCatalog catalog, PriceCatalog prices, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = configuration.GetSection(FirmOptions.SectionName).Get<List<FirmOptions>>() ?? [];
        foreach (var firmOptions in options)
        {
            var firm = firms.ById(firmOptions.Id)!;
            foreach (var seed in firmOptions.SeedChallenges)
            {
                var definition = seed.Template switch
                {
                    SeedChallengeOptions.TwoStepTemplate => ChallengeTemplates.TwoStep(seed.Id, seed.InitialBalance, seed.Currency),
                    SeedChallengeOptions.QuickTestTemplate => QuickTest(seed),
                    _ => throw new InvalidOperationException($"Firm {firm.Id} seeds challenge {seed.Id} from unknown template {seed.Template}."),
                };
                if (ChallengeCatalog.Validate(definition) is { Count: > 0 } errors)
                {
                    throw new InvalidOperationException($"Firm {firm.Id} seeds an invalid challenge {seed.Id}: {string.Join(" ", errors)}");
                }

                await catalog.SaveAsync(firm.Id, definition, cancellationToken);
                if (seed.Price is { } price)
                {
                    if (PriceRules.Problem(price, seed.Currency) is { } problem)
                    {
                        throw new InvalidOperationException($"Firm {firm.Id} seeds challenge {seed.Id} with an invalid price: {problem}");
                    }

                    await prices.SaveAsync(firm.Id, new ChallengePrice(seed.Id, price, seed.Currency, ForSale: true), cancellationToken);
                }
                else
                {
                    await prices.RemoveAsync(firm.Id, seed.Id, cancellationToken);
                }
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// The two-step challenge with 0.1 % profit targets and no minimum trading days, so the whole way to a
    /// payout can be tried in minutes. For development only.
    /// </summary>
    internal static ChallengeDefinition QuickTest(SeedChallengeOptions seed)
    {
        var template = ChallengeTemplates.TwoStep(seed.Id, seed.InitialBalance, seed.Currency);
        return template with
        {
            Name = $"Quick test {ChallengeTemplates.SizeName(seed.InitialBalance)}",
            Evaluation = [.. template.Evaluation.Select(s => s with { ProfitTargetPercent = 0.1m, MinTradingDays = 0 })],
            Funded = template.Funded with { MinTradingDays = 0 },
        };
    }
}
