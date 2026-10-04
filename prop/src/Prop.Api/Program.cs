using System.Reflection;
using System.Threading.RateLimiting;

using Common.Postgres;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

using Npgsql;

using Prop.Api.Api;
using Prop.Api.Billing;
using Prop.Api.Challenges;
using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Firms;
using Prop.Api.Json;
using Prop.Api.Ops;
using Prop.Api.Payments;
using Prop.Api.Persistence;
using Prop.Api.Portal;
using Prop.Api.Review;
using Prop.Api.Signup;
using Prop.Api.Trading;

// The build-time OpenAPI generator loads the app only to read its endpoints.
var isOpenApiGeneration = Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider";

var builder = WebApplication.CreateBuilder(args);

var tradingPlatform = builder.Services.AddOptions<TradingPlatformOptions>()
    .Bind(builder.Configuration.GetSection(TradingPlatformOptions.SectionName))
    .Validate(
        o => o.Url is { IsAbsoluteUri: true } url && url.AbsolutePath.EndsWith('/'),
        "TradingPlatform:Url must be the trading service's absolute address, ending with /.")
    .Validate(o => o.EventWaitSeconds is >= 0 and <= 30 && o.EventsPerRequest is >= 1 and <= 1_000, "TradingPlatform event settings are out of range.")
    .Validate(o => o.PartnerApiKey.Length > 0, "TradingPlatform:PartnerApiKey is required, so firms that sign up get a trading server.");
var platform = builder.Services.AddOptions<PlatformOptions>()
    .Bind(builder.Configuration.GetSection(PlatformOptions.SectionName))
    .Validate(
        o => o.Url is { IsAbsoluteUri: true } url && url.AbsolutePath.EndsWith('/'),
        "Platform:Url must be the absolute address where firms sign up, ending with /.")
    .Validate(
        o => o.FirmPortalUrl.Contains("{firm}", StringComparison.Ordinal)
            && Uri.TryCreate(o.FirmPortalUrl.Replace("{firm}", "firm", StringComparison.Ordinal), UriKind.Absolute, out var portal)
            && portal.AbsolutePath.EndsWith('/'),
        "Platform:FirmPortalUrl must be an absolute address with {firm} for the short name, ending with /.")
    .Validate(
        o => o.ApiUrl is { IsAbsoluteUri: true } url && url.AbsolutePath.EndsWith('/'),
        "Platform:ApiUrl must be the absolute address where the internet reaches this service, ending with /.")
    .Validate(
        o => o.OpsUrl is { IsAbsoluteUri: true } url && url.AbsolutePath.EndsWith('/')
            && !string.Equals(url.Host, o.Url?.Host, StringComparison.OrdinalIgnoreCase),
        "Platform:OpsUrl must be the absolute address of our admin view, on its own host, ending with /.");
var email = builder.Services.AddOptions<EmailOptions>()
    .Bind(builder.Configuration.GetSection(EmailOptions.SectionName))
    .Validate(
        o => o.From.Contains('@', StringComparison.Ordinal) && o.Smtp.Host.Length > 0 && Enum.TryParse<MailKit.Security.SecureSocketOptions>(o.Smtp.Security, out _),
        "Email needs From, Smtp:Host and Smtp:Security (None, StartTls or SslOnConnect).");
builder.Services.AddOptions<SignupOptions>().Bind(builder.Configuration.GetSection(SignupOptions.SectionName));
builder.Services.AddOptions<SandboxOptions>()
    .Bind(builder.Configuration.GetSection(SandboxOptions.SectionName))
    .Validate(o => o.MaxOpenAccounts >= 1, "Sandbox:MaxOpenAccounts must be at least 1.")
    .ValidateOnStart();
builder.Services.AddOptions<SecretsOptions>().Bind(builder.Configuration.GetSection(SecretsOptions.SectionName));
builder.Services.AddOptions<StaffOptions>().Bind(builder.Configuration.GetSection(StaffOptions.SectionName));
builder.Services.AddOptions<PaymentsOptions>()
    .Bind(builder.Configuration.GetSection(PaymentsOptions.SectionName))
    .Validate(
        o => o.OrderLifetime >= TimeSpan.FromMinutes(30) && o.OrderLifetime <= TimeSpan.FromHours(23) && o.StripeApiUrl.IsAbsoluteUri && o.StripeApiUrl.AbsolutePath.EndsWith('/'),
        "Payments:OrderLifetime must be 30 minutes to 23 hours, as Stripe allows, and Payments:StripeApiUrl an absolute address ending with /.")
    .ValidateOnStart();
var billingOptions = builder.Services.AddOptions<BillingOptions>()
    .Bind(builder.Configuration.GetSection(BillingOptions.SectionName))
    .Validate(
        o => BillingTerms.From(o).Problems().Count == 0,
        "Billing has invalid prices or slot rules. Check Billing:Currency, StartupFee, ReviewDeposit, PackagePrice, PackageSlots, SlotPrices, MaxSlots and ChargeDaysBeforeMonth.")
    .Validate(
        o => o.RetryInterval > TimeSpan.Zero && o.MaxAttempts >= 1 && o.WarningPercent is >= 1 and <= 100
            && o.CheckoutLifetime >= TimeSpan.FromMinutes(30) && o.CheckoutLifetime <= TimeSpan.FromHours(23),
        "Billing needs a positive RetryInterval, MaxAttempts of at least 1, WarningPercent of 1 to 100 and a CheckoutLifetime of 30 minutes to 23 hours.")
    .Validate(
        o => o.Provider == nameof(BillingProvider.Test) || (o.Provider == nameof(BillingProvider.Stripe) && o.StripeSecretKey.Length > 0 && o.StripeWebhookSecret.Length > 0),
        "Billing:Provider must be Stripe, with Billing:StripeSecretKey and Billing:StripeWebhookSecret, or Test for development.");
if (!isOpenApiGeneration)
{
    tradingPlatform.ValidateOnStart();
    platform.ValidateOnStart();
    email.ValidateOnStart();
    billingOptions.ValidateOnStart();
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<FirmCatalog>();
builder.Services.AddSingleton<FirmStore>();
builder.Services.AddSingleton<SecretProtector>();
builder.Services.AddSingleton<FirmAdmins>();
builder.Services.AddSingleton<SignupService>();
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// Resolved lazily, so tools that only load the app (like the OpenAPI generator) need no database.
builder.Services.AddSingleton(sp => NpgsqlDataSource.Create(
    sp.GetRequiredService<IConfiguration>().GetConnectionString("Prop")
    ?? throw new InvalidOperationException("The connection string Prop is missing.")));
builder.Services.AddSingleton(PropMigrations.All);
builder.Services.AddSingleton<DatabaseSchema>();
builder.Services.AddSingleton<WorkSignals>();
builder.Services.AddSingleton<TradingStreamProgress>();
builder.Services.AddSingleton<ChallengeCatalog>();
builder.Services.AddSingleton<ChallengeService>();
builder.Services.AddSingleton<ChallengeQueries>();
builder.Services.AddSingleton<PayoutQueries>();
builder.Services.AddSingleton<PriceCatalog>();
builder.Services.AddSingleton<OrderStore>();
builder.Services.AddSingleton<OrderService>();
builder.Services.AddSingleton<StripeClient>();
builder.Services.AddSingleton<SlotService>();
builder.Services.AddSingleton<BillingStore>();
builder.Services.AddSingleton<BillingService>();
builder.Services.AddSingleton<IBillingGateway>(sp =>
    sp.GetRequiredService<IOptions<BillingOptions>>().Value.Provider == nameof(BillingProvider.Test)
        ? new TestBillingGateway()
        : ActivatorUtilities.CreateInstance<StripeBillingGateway>(sp));
builder.Services.AddSingleton<PortalUsers>();
builder.Services.AddSingleton<IPasswordHasher<PortalUser>, PasswordHasher<PortalUser>>();
builder.Services.AddSingleton<StaffUsers>();
builder.Services.AddSingleton<IPasswordHasher<StaffUser>, PasswordHasher<StaffUser>>();
builder.Services.AddSingleton<StaffNotifier>();
builder.Services.AddSingleton<ReviewStore>();
builder.Services.AddSingleton<ReviewService>();
builder.Services.AddSingleton<OpsFirms>();

// Portal sessions survive restarts and work across instances, since the keys that protect them are in the database.
builder.Services.AddSingleton<IXmlRepository, PostgresXmlRepository>();
builder.Services.AddDataProtection().SetApplicationName("prop-api");
if (!isOpenApiGeneration)
{
    builder.Services.AddOptions<KeyManagementOptions>().Configure<IXmlRepository>((options, repository) => options.XmlRepository = repository);
}

builder.Services.AddOptions<LoginOptions>()
    .Bind(builder.Configuration.GetSection(LoginOptions.SectionName))
    .Validate(
        o => o.MinimumPasswordLength >= 1 && o.AttemptsPerMinute >= 0 && o.SessionLifetime > TimeSpan.Zero,
        "Login needs a password length of at least 1, attempts per minute of 0 or more and a positive session lifetime.")
    .ValidateOnStart();
builder.Services.AddPortalAuth();
builder.Services.AddStaffAuth();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(PortalAuth.LoginRateLimit, context =>
    {
        var address = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var attempts = context.RequestServices.GetRequiredService<IOptions<LoginOptions>>().Value.AttemptsPerMinute;
        return attempts == 0
            ? RateLimitPartition.GetNoLimiter(address)
            : RateLimitPartition.GetFixedWindowLimiter(address, _ => new FixedWindowRateLimiterOptions { PermitLimit = attempts, Window = TimeSpan.FromMinutes(1) });
    });
});

// Long enough for the trading platform's longest wait for new events.
builder.Services.AddHttpClient(TradingPlatformClient.HttpClientName, (sp, client) =>
{
    client.BaseAddress = sp.GetRequiredService<IOptions<TradingPlatformOptions>>().Value.Url;
    client.Timeout = TimeSpan.FromSeconds(60);
});
builder.Services.AddSingleton<ITradingPlatform, TradingPlatformClient>();
builder.Services.AddSingleton<ITradingPartner, TradingPartnerClient>();
builder.Services.AddHttpClient(WebhookWorker.HttpClientName, client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddHttpClient(StripeClient.HttpClientName, (sp, client) =>
{
    client.BaseAddress = sp.GetRequiredService<IOptions<PaymentsOptions>>().Value.StripeApiUrl;
    client.Timeout = TimeSpan.FromSeconds(20);
});

if (!isOpenApiGeneration)
{
    // The seeders run first, so the firms and their challenges exist before anything else starts.
    builder.Services.AddHostedService<FirmSeeder>();
    builder.Services.AddHostedService<ChallengeSeeder>();
    builder.Services.AddHostedService<PortalSeeder>();
    builder.Services.AddHostedService<StaffSeeder>();
    builder.Services.AddHostedService<TradingEventConsumer>();
    builder.Services.AddHostedService<TradingCommandWorker>();
    builder.Services.AddHostedService<TradingDayScheduler>();
    builder.Services.AddHostedService<WebhookWorker>();
    builder.Services.AddHostedService<FirmProvisioner>();
    builder.Services.AddHostedService<BillingWorker>();
}

builder.Services.ConfigureHttpJsonOptions(o => PropJson.Configure(o.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks().AddCheck<FirmsHealthCheck>("firms");

var app = builder.Build();

// Not yet hardened for production: HTTPS and secrets management are missing.
// The OpenAPI generator never serves requests, so it is allowed.
if (!app.Environment.IsDevelopment() && !isOpenApiGeneration)
{
    throw new InvalidOperationException("Prop.Api is not yet ready for production and may only run in the Development environment.");
}

// Browsers reach the portal API through the portal, and in production through a proxy in front of it that
// adds their address and scheme. The login limit counts each browser, and the session cookie is secure behind
// HTTPS. Only loopback proxies are trusted until production networks are configured.
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseOpsHost();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapFirmApi();
app.MapPortalApi();
app.MapSignupApi();
app.MapOpsApi();
app.MapPaymentWebhooks();
app.MapBillingWebhooks();

app.Run();
