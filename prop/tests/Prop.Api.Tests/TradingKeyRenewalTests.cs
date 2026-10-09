using System.Collections.Concurrent;
using System.Net;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Prop.Api.Firms;
using Prop.Api.Tests.Support;
using Prop.Api.Trading;

namespace Prop.Api.Tests;

/// <summary>
/// A firm whose admin key the trading platform refuses, for example after our staff stopped it in the platform's staff
/// panel, gets a new one through the partner API by itself. The real client and renewal, against servers that take only
/// the newest key the fake trading platform gave out.
/// </summary>
public sealed class TradingKeyRenewalTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task AStoppedKeyIsRenewedOnceSavedAndUsedFromThenOn()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var stopped = await SignUpAsync(factory, "acme");
        factory.Trading.StopKey("acme");
        var servers = new ServerStub(factory.Trading, "acme");
        var log = new WarningLog();
        var client = Client(servers, Renewal(factory, log: log));

        var account = await client.GetAccountAsync(stopped, "A1", TestContext.Current.CancellationToken);
        var renewed = Catalog(factory).ById("acme")!.Trading!;
        await client.GetAccountAsync(renewed, "A1", TestContext.Current.CancellationToken);

        // A request that started with the stopped key gets the new one without another renewal.
        await client.GetAccountAsync(stopped, "A1", TestContext.Current.CancellationToken);

        Assert.NotNull(account);
        Assert.Equal(stopped with { ApiKey = factory.Trading.KeyOf("acme")! }, renewed);
        Assert.Equal([stopped.ApiKey, renewed.ApiKey, renewed.ApiKey, stopped.ApiKey, renewed.ApiKey], servers.Keys);
        Assert.Single(factory.Trading.Commands, c => c == "replace key acme");

        // Saved encrypted, as when the server was created.
        var saved = await factory.Services.GetRequiredService<FirmStore>().GetAsync("acme", TestContext.Current.CancellationToken);
        Assert.Equal(renewed, saved!.Trading);
        Assert.DoesNotContain(renewed.ApiKey, (string)(await factory.ScalarAsync("select trading_api_key from firms where id = 'acme'"))!, StringComparison.Ordinal);

        // The warning names the firm, never a key.
        var warning = Assert.Single(log.Messages);
        Assert.Contains("acme", warning, StringComparison.Ordinal);
        Assert.DoesNotContain(stopped.ApiKey, warning, StringComparison.Ordinal);
        Assert.DoesNotContain(renewed.ApiKey, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequestsRefusedAtTheSameTimeShareOneNewKey()
    {
        const int requests = 5;
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var stopped = await SignUpAsync(factory, "acme");
        factory.Trading.StopKey("acme");
        var servers = new ServerStub(factory.Trading, "acme") { HoldRefusals = requests };
        var client = Client(servers, factory.Services.GetRequiredService<ITradingKeyRenewal>());

        var accounts = await Eventually.Within(
            Task.WhenAll(Enumerable.Range(0, requests).Select(_ => client.GetAccountAsync(stopped, "A1", TestContext.Current.CancellationToken))));

        Assert.All(accounts, a => Assert.NotNull(a));
        Assert.Single(factory.Trading.Commands, c => c == "replace key acme");
        Assert.Equal(requests, servers.Keys.Count(k => k == stopped.ApiKey));
        Assert.Equal(requests, servers.Keys.Count(k => k == factory.Trading.KeyOf("acme")));
        Assert.Equal(factory.Trading.KeyOf("acme"), Catalog(factory).ById("acme")!.Trading!.ApiKey);
    }

    [Fact]
    public async Task ARenewalHoldsBackNoOtherFirm()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var acme = await SignUpAsync(factory, "acme");
        var beta = await SignUpAsync(factory, "beta");
        factory.Trading.StopKey("acme");
        factory.Trading.StopKey("beta");
        var acmeMayRenew = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var partner = new StubPartner(async server =>
        {
            if (server == "acme")
            {
                await acmeMayRenew.Task;
            }

            return await factory.Trading.ReplaceAdminKeyAsync(server, CancellationToken.None);
        });
        var client = Client(new ServerStub(factory.Trading, "acme", "beta"), Renewal(factory, partner));

        var acmeAccount = client.GetAccountAsync(acme, "A1", TestContext.Current.CancellationToken);
        await Eventually.ThatAsync(() => partner.Asked.Contains("acme"), "the renewal of acme's key to start");
        var betaAccount = await Eventually.Within(client.GetAccountAsync(beta, "A1", TestContext.Current.CancellationToken));
        var acmeWaited = !acmeAccount.IsCompleted;
        acmeMayRenew.SetResult();

        Assert.NotNull(betaAccount);
        Assert.True(acmeWaited);
        Assert.NotNull(await Eventually.Within(acmeAccount));
    }

    // Its key comes from the configuration, and its server is not one the prop platform created through the partner API.
    [Fact]
    public async Task AConfiguredFirmsRefusedKeyStaysRefused()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var catalog = Catalog(factory);
        await catalog.Ready.WaitAsync(TestContext.Current.CancellationToken);
        var demo = catalog.ById("demo-firm")!.Trading!;
        var servers = new ServerStub(factory.Trading);

        await Assert.ThrowsAsync<TradingPlatformRejectedException>(
            () => Client(servers, Renewal(factory)).GetAccountAsync(demo, "A1", TestContext.Current.CancellationToken));

        Assert.Equal([demo.ApiKey], servers.Keys);
        Assert.DoesNotContain(factory.Trading.Commands, c => c.StartsWith("replace key", StringComparison.Ordinal));
        Assert.Equal(demo, Catalog(factory).ById("demo-firm")!.Trading);
    }

    // The partner API answers 404 for a server that is not the prop platform's.
    [Fact]
    public async Task ARefusedRenewalLeavesTheRefusalAndTheKey()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var stopped = await SignUpAsync(factory, "acme");
        factory.Trading.StopKey("acme");
        var servers = new ServerStub(factory.Trading, "acme");
        var partner = new StubPartner(server => throw new TradingPlatformRejectedException($"The trading platform refused POST api/partner/v1/tenants/{server}/admin-key: 404"));

        var refused = await Assert.ThrowsAsync<TradingPlatformRejectedException>(
            () => Client(servers, Renewal(factory, partner)).GetAccountAsync(stopped, "A1", TestContext.Current.CancellationToken));

        Assert.Contains("api/admin/v1/accounts/A1: 401", refused.Message, StringComparison.Ordinal);
        Assert.Equal([stopped.ApiKey], servers.Keys);
        Assert.Equal(["acme"], partner.Asked);
        Assert.Equal(stopped, Catalog(factory).ById("acme")!.Trading);
    }

    // The call can be tried again later, and then asks for a new key again.
    [Fact]
    public async Task ARenewalThatCannotReachThePlatformFailsTheCallForNow()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var stopped = await SignUpAsync(factory, "acme");
        factory.Trading.StopKey("acme");
        var servers = new ServerStub(factory.Trading, "acme");
        var partner = new StubPartner(_ => throw new TradingPlatformUnavailableException("The trading platform could not be reached."));

        await Assert.ThrowsAsync<TradingPlatformUnavailableException>(
            () => Client(servers, Renewal(factory, partner)).GetAccountAsync(stopped, "A1", TestContext.Current.CancellationToken));

        Assert.Equal([stopped.ApiKey], servers.Keys);
        Assert.Equal(stopped, Catalog(factory).ById("acme")!.Trading);
    }

    /// <summary>Signs the firm up and waits until it has its server. Returns the firm's server and key as the catalog has them.</summary>
    private static async Task<FirmTrading> SignUpAsync(PropFactory factory, string firmId)
    {
        using var admin = await factory.SignUpAsync(firmId, $"owner@{firmId}.test");
        await Eventually.ThatAsync(() => Catalog(factory).ById(firmId)?.Trading is not null, $"{firmId} to get its trading server");
        return Catalog(factory).ById(firmId)!.Trading!;
    }

    private static FirmCatalog Catalog(PropFactory factory) => factory.Services.GetRequiredService<FirmCatalog>();

    private static TradingKeyRenewal Renewal(PropFactory factory, ITradingPartner? partner = null, ILogger<TradingKeyRenewal>? log = null) =>
        new(Catalog(factory), factory.Services.GetRequiredService<FirmStore>(), partner ?? factory.Trading, factory.Time, log ?? NullLogger<TradingKeyRenewal>.Instance);

    private static TradingPlatformClient Client(ServerStub servers, ITradingKeyRenewal keys) => new(new Factory(servers), keys);

    private sealed class Factory(ServerStub servers) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(servers, disposeHandler: false) { BaseAddress = new Uri("https://trading.test/") };
    }

    /// <summary>
    /// The admin API of the servers: a key is taken only while it is the newest one the fake trading platform gave its
    /// server, as on ours, and every account is worth its starting balance. Refusals can be held until several requests
    /// are refused at once.
    /// </summary>
    private sealed class ServerStub(FakeTradingPlatform trading, params string[] servers) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _refusalsHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _refusals;

        /// <summary>The key of every request, in order.</summary>
        public ConcurrentQueue<string> Keys { get; } = new();

        /// <summary>How many refused requests there must be before any of them is answered.</summary>
        public int HoldRefusals { get; init; } = 1;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = request.Headers.GetValues("X-Api-Key").Single();
            Keys.Enqueue(key);
            if (servers.Any(s => trading.KeyOf(s) == key))
            {
                return Answer(request, HttpStatusCode.OK, """{"accountId":"A1","balance":100000,"equity":100000,"floors":[]}""");
            }

            if (Interlocked.Increment(ref _refusals) >= HoldRefusals)
            {
                _refusalsHeld.TrySetResult();
            }

            await _refusalsHeld.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return Answer(request, HttpStatusCode.Unauthorized, """{"status":401,"title":"A valid X-Api-Key header is required."}""");
        }

        private static HttpResponseMessage Answer(HttpRequestMessage request, HttpStatusCode status, string body) =>
            new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json"), RequestMessage = request };
    }

    /// <summary>A partner API that makes new admin keys with <paramref name="replace"/>, and records whose it was asked for.</summary>
    private sealed class StubPartner(Func<string, Task<string>> replace) : ITradingPartner
    {
        public ConcurrentQueue<string> Asked { get; } = new();

        public Task<string> ReplaceAdminKeyAsync(string server, CancellationToken cancellationToken)
        {
            Asked.Enqueue(server);
            return replace(server);
        }

        public Task<bool> IsServerAvailableAsync(string server, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PartnerTenant?> CreateTenantAsync(string server, string name, string currency, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PartnerTenant?> GetTenantAsync(string server, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetListingAsync(string server, bool listed, Uri loginUrl, Uri? logoUrl, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<PriceFeedStatus> GetPriceFeedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    /// <summary>The warnings logged, with their exceptions.</summary>
    private sealed class WarningLog : ILogger<TradingKeyRenewal>
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Messages.Enqueue($"{formatter(state, exception)} {exception}");
            }
        }
    }
}
