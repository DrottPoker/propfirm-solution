using Prop.Api.Configuration;
using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Challenges;

/// <summary>Creates each firm's configured challenges at startup, unless the firm already has them.</summary>
internal sealed class ChallengeSeeder(FirmCatalog firms, ChallengeCatalog catalog, IConfiguration configuration) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var options = configuration.GetSection(FirmOptions.SectionName).Get<List<FirmOptions>>() ?? [];
        foreach (var firmOptions in options)
        {
            var firm = firms.ById(firmOptions.Id)!;
            foreach (var seed in firmOptions.SeedChallenges)
            {
                var definition = seed.Template == SeedChallengeOptions.TwoStepTemplate
                    ? ChallengeTemplates.TwoStep(seed.Id, seed.InitialBalance, seed.Currency)
                    : throw new InvalidOperationException($"Firm {firm.Id} seeds challenge {seed.Id} from unknown template {seed.Template}.");
                if (ChallengeCatalog.Validate(definition) is { Count: > 0 } errors)
                {
                    throw new InvalidOperationException($"Firm {firm.Id} seeds an invalid challenge {seed.Id}: {string.Join(" ", errors)}");
                }

                await catalog.AddIfMissingAsync(firm.Id, definition, cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
