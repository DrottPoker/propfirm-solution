using System.Text.RegularExpressions;

using Prop.Api.Configuration;
using Prop.Api.Payments;

namespace Prop.Api.Firms;

/// <summary>
/// Saves the configured firms to the database at startup, as the configuration has them, and loads every firm
/// into the catalog. Startup fails if the configuration is invalid.
/// </summary>
internal sealed partial class FirmSeeder(FirmStore store, FirmCatalog catalog, IConfiguration configuration, TimeProvider time) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var firms = (configuration.GetSection(FirmOptions.SectionName).Get<List<FirmOptions>>() ?? []).Select(Validate).ToList();
            Require(firms.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() == firms.Count, "Firm ids must be unique.");
            Require(
                firms.SelectMany(f => f.Portal.Hosts).Distinct(StringComparer.OrdinalIgnoreCase).Count() == firms.Sum(f => f.Portal.Hosts.Count),
                "A portal host can belong to one firm only.");

            foreach (var firm in firms)
            {
                await store.SaveConfiguredAsync(firm, time.GetUtcNow(), cancellationToken);
            }

            catalog.Load(await store.ListAsync(cancellationToken));
        }
        catch (Exception exception)
        {
            catalog.LoadFailed(exception);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static Firm Validate(FirmOptions options)
    {
        Require(FirmRules.IsValidId(options.Id), $"Firm id '{options.Id}' must be 2 to 40 lowercase letters, digits or dashes, not first or last.");
        Require(FirmRules.IsValidName(options.Name), $"Firm {options.Id} needs a name of at most {FirmRules.MaxNameLength} characters.");
        Require(Sha256Hex().IsMatch(options.ApiKeySha256), $"Firm {options.Id} needs ApiKeySha256 as 64 lowercase hex characters.");
        Require(
            options.Trading.Server.Length > 0 && options.Trading.ApiKey.Length > 0 && options.Trading.Group.Length > 0 && options.Trading.Currency.Length > 0,
            $"Firm {options.Id} needs a trading server, API key, group and currency.");
        Require(
            options.Webhook.Url is null || (options.Webhook.Url.IsAbsoluteUri && options.Webhook.Secret.Length >= 32),
            $"Firm {options.Id}: a webhook needs an absolute address and a secret of at least 32 characters.");

        var portal = options.Portal;
        Require(
            portal.Url is { IsAbsoluteUri: true } url && url.AbsolutePath.EndsWith('/') && portal.Hosts.Count > 0,
            $"Firm {options.Id} needs a portal address ending with / and at least one portal host.");
        Require(FirmRules.IsValidLogoUrl(portal.LogoUrl), $"Firm {options.Id}: the logo must be an absolute https address.");
        Require(FirmRules.ColorProblem(portal.Colors) is null, $"Firm {options.Id}: {FirmRules.ColorProblem(portal.Colors)}");

        var payments = options.Payments;
        PaymentProvider? provider = null;
        if (payments.Provider.Length > 0)
        {
            Require(Enum.TryParse<PaymentProvider>(payments.Provider, out var parsed), $"Firm {options.Id}: the payment provider must be Test, Stripe or External.");
            provider = parsed;
        }

        var hasStripeKeys = payments.StripeSecretKey.Length > 0 || payments.StripeWebhookSecret.Length > 0;
        Require(
            !hasStripeKeys || (StripeKeyRules.IsValidSecretKey(payments.StripeSecretKey) && StripeKeyRules.IsValidWebhookSecret(payments.StripeWebhookSecret)),
            $"Firm {options.Id}: Stripe needs a secret key (sk_ or rk_) and a webhook signing secret (whsec_).");
        Require(provider != PaymentProvider.Stripe || hasStripeKeys, $"Firm {options.Id}: Stripe payments need the Stripe keys.");
        Require(
            payments.CheckoutUrl is null || payments.CheckoutUrl is { IsAbsoluteUri: true, Scheme: "https" },
            $"Firm {options.Id}: the checkout page must be an absolute https address.");
        Require(provider != PaymentProvider.External || payments.CheckoutUrl is not null, $"Firm {options.Id}: External payments need the checkout page.");
        Require(payments.TermsUrl is null || payments.TermsUrl is { IsAbsoluteUri: true, Scheme: "https" }, $"Firm {options.Id}: the terms must be an absolute https address.");

        var name = options.Name.Trim();
        return new Firm(
            options.Id,
            name,
            FirmStatus.Live,
            Convert.FromHexString(options.ApiKeySha256),
            new FirmTrading(options.Trading.Server, options.Trading.ApiKey, options.Trading.Group, options.Trading.Currency),
            options.Webhook.Url is { } webhook ? new FirmWebhook(webhook, options.Webhook.Secret) : null,
            new FirmPortal(portal.Url!, [.. portal.Hosts], new Branding(name, portal.LogoUrl.Length > 0 ? portal.LogoUrl : null, portal.Colors.ToDictionary())),
            new FirmPayments(
                provider,
                hasStripeKeys ? new StripeKeys(payments.StripeSecretKey, payments.StripeWebhookSecret) : null,
                payments.CheckoutUrl,
                payments.TermsUrl));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Invalid firm configuration: {message}");
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Hex();
}
