using System.Xml.Linq;

using Common.Postgres;

using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using Trading.Service.Identity;
using Trading.Service.Persistence;
using Trading.Service.Tenancy;
using Trading.Service.Tests.Support;

namespace Trading.Service.Tests;

// Every test starts from an empty database, so each store must create the schema itself when used first.
public sealed class PostgresIdentityTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task UsersAreFoundByEmailWithoutCaseAndOnlyWithinTheirFirm()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var users = new PostgresUserStore(dataSource, Schema(dataSource));

        var created = await users.CreateAsync("firm-a", " Trader@Test.Example ", "hash", TestContext.Current.CancellationToken);

        Assert.NotNull(created);
        Assert.Equal("Trader@Test.Example", created.Email);
        Assert.Equal(created, await users.FindByEmailAsync("firm-a", "trader@test.example", TestContext.Current.CancellationToken));
        Assert.Null(await users.FindByEmailAsync("firm-b", "trader@test.example", TestContext.Current.CancellationToken));
        Assert.Equal(created, await users.FindByIdAsync(created.Id, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmailIsUniqueWithinAFirmOnly()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var users = new PostgresUserStore(dataSource, Schema(dataSource));

        Assert.NotNull(await users.CreateAsync("firm-a", "a@test.example", "hash", TestContext.Current.CancellationToken));
        Assert.Null(await users.CreateAsync("firm-a", "A@TEST.EXAMPLE", "hash", TestContext.Current.CancellationToken));
        Assert.NotNull(await users.CreateAsync("firm-b", "a@test.example", "hash", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnAccountHasOneOwner()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var users = new PostgresUserStore(dataSource, Schema(dataSource));
        var first = (await users.CreateAsync("firm-a", "a@test.example", "hash", TestContext.Current.CancellationToken))!;
        var second = (await users.CreateAsync("firm-a", "b@test.example", "hash", TestContext.Current.CancellationToken))!;

        Assert.True(await users.AddAccountAsync(first.Id, "A1", TestContext.Current.CancellationToken));
        Assert.False(await users.AddAccountAsync(second.Id, "A1", TestContext.Current.CancellationToken));
        Assert.True(await users.OwnsAsync(first.Id, "A1", TestContext.Current.CancellationToken));
        Assert.False(await users.OwnsAsync(second.Id, "A1", TestContext.Current.CancellationToken));
        Assert.Equal(["A1"], await users.AccountsOfAsync(first.Id, TestContext.Current.CancellationToken));

        await users.RemoveAccountAsync("A1", TestContext.Current.CancellationToken);
        Assert.False(await users.OwnsAsync(first.Id, "A1", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CookieKeysAreKept()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var keys = new PostgresXmlRepository(dataSource, Schema(dataSource));

        keys.StoreElement(new XElement("key", new XAttribute("id", "1")), "key-1");
        keys.StoreElement(new XElement("key", new XAttribute("id", "1")), "key-1");
        keys.StoreElement(new XElement("key", new XAttribute("id", "2")), "key-2");

        Assert.Equal(["1", "2"], new PostgresXmlRepository(dataSource, Schema(dataSource)).GetAllElements().Select(e => e.Attribute("id")!.Value));
    }

    // The service reads the cookie keys before the engine starts, so this is the first use on a new database.
    [Fact]
    public async Task CookieKeysCanBeReadFirstOnANewDatabase()
    {
        await using var dataSource = await CreateDatabaseAsync();

        Assert.Empty(new PostgresXmlRepository(dataSource, Schema(dataSource)).GetAllElements());
    }

    [Fact]
    public async Task APasswordCanBeReplaced()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var users = new PostgresUserStore(dataSource, Schema(dataSource));
        var user = (await users.CreateAsync("firm-a", "a@test.example", "old-hash", TestContext.Current.CancellationToken))!;

        Assert.True(await users.SetPasswordHashAsync(user.Id, "new-hash", TestContext.Current.CancellationToken));
        Assert.False(await users.SetPasswordHashAsync(Guid.NewGuid(), "new-hash", TestContext.Current.CancellationToken));
        Assert.Equal("new-hash", (await users.FindByIdAsync(user.Id, TestContext.Current.CancellationToken))!.PasswordHash);
    }

    [Fact]
    public async Task LoginLinksWorkOnceBeforeTheyExpire()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var schema = Schema(dataSource);
        var user = (await new PostgresUserStore(dataSource, schema).CreateAsync("firm-a", "a@test.example", "hash", TestContext.Current.CancellationToken))!;
        var links = new PostgresLoginLinkStore(dataSource, schema);
        var now = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
        byte[] link = [1, 2, 3];
        byte[] expiring = [4, 5, 6];

        await links.CreateAsync(link, user.Id, "A1", now.AddMinutes(2), TestContext.Current.CancellationToken);
        await links.CreateAsync(expiring, user.Id, null, now.AddMinutes(2), TestContext.Current.CancellationToken);

        Assert.Equal(new LoginLink(user.Id, "A1"), await links.UseAsync(link, now, TestContext.Current.CancellationToken));
        Assert.Null(await links.UseAsync(link, now, TestContext.Current.CancellationToken));
        Assert.Null(await links.UseAsync(expiring, now.AddMinutes(2), TestContext.Current.CancellationToken));
        Assert.Null(await links.UseAsync([9, 9, 9], now, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LinksThatExpiredLongAgoAreRemovedWhenNewOnesAreMade()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var schema = Schema(dataSource);
        var user = (await new PostgresUserStore(dataSource, schema).CreateAsync("firm-a", "a@test.example", "hash", TestContext.Current.CancellationToken))!;
        var links = new PostgresLoginLinkStore(dataSource, schema);
        var now = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);

        await links.CreateAsync([1], user.Id, null, now.AddDays(-2), TestContext.Current.CancellationToken);
        await links.CreateAsync([2], user.Id, null, now.AddHours(-1), TestContext.Current.CancellationToken);
        await links.CreateAsync([3], user.Id, null, now.AddMinutes(2), TestContext.Current.CancellationToken);

        await using var count = dataSource.CreateCommand("select count(*) from login_links");
        Assert.Equal(2L, (long)(await count.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }

    [Fact]
    public async Task FirmsKeepTheirGroupsAndKeys()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var tenants = new PostgresTenantStore(dataSource, Schema(dataSource));
        var now = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
        var configured = new Tenant("demo-firm", "Demo Firm", ["standard"], [1], null, Listed: true);
        var created = new Tenant("acme", "Acme", ["acme-standard", "acme-eur"], [2], "prop-platform", Listed: false);

        await tenants.SaveConfiguredAsync(configured, now, TestContext.Current.CancellationToken);
        await tenants.SaveConfiguredAsync(configured with { Name = "Demo", Groups = ["standard", "gold"] }, now, TestContext.Current.CancellationToken);
        Assert.True(await tenants.CreateAsync(created, now, TestContext.Current.CancellationToken));
        await tenants.SetAdminApiKeyAsync("acme", [3], TestContext.Current.CancellationToken);

        var all = await tenants.ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["acme", "demo-firm"], all.Select(t => t.Id));
        Assert.Equal(["acme-eur", "acme-standard"], all[0].Groups);
        Assert.Equal([3], all[0].AdminApiKeyHash);
        Assert.Equal(("prop-platform", false), (all[0].PartnerId, all[0].Listed));
        Assert.Equal(["gold", "standard"], all[1].Groups);
        Assert.Equal(("Demo", (string?)null, true), (all[1].Name, all[1].PartnerId, all[1].Listed));
    }

    [Fact]
    public async Task FirmIdsAndGroupsAreTakenOnce()
    {
        await using var dataSource = await CreateDatabaseAsync();
        var tenants = new PostgresTenantStore(dataSource, Schema(dataSource));
        var now = new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
        await tenants.SaveConfiguredAsync(new Tenant("demo-firm", "Demo Firm", ["standard"], [1], null, true), now, TestContext.Current.CancellationToken);
        Assert.True(await tenants.CreateAsync(new Tenant("acme", "Acme", ["acme-standard"], [2], "prop-platform", false), now, TestContext.Current.CancellationToken));

        Assert.False(await tenants.CreateAsync(new Tenant("acme", "Acme", ["other-group"], [3], "prop-platform", false), now, TestContext.Current.CancellationToken));
        Assert.False(await tenants.CreateAsync(new Tenant("acme-two", "Acme", ["standard"], [3], "prop-platform", false), now, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenants.SaveConfiguredAsync(new Tenant("acme", "Taken over", ["gold"], [4], null, true), now, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenants.SaveConfiguredAsync(new Tenant("other-firm", "Other", ["acme-standard"], [4], null, true), now, TestContext.Current.CancellationToken));

        var all = await tenants.ListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([("acme", "Acme"), ("demo-firm", "Demo Firm")], all.Select(t => (t.Id, t.Name)));
    }

    private static DatabaseSchema Schema(NpgsqlDataSource dataSource) => new(dataSource, TradingMigrations.All, NullLogger<DatabaseSchema>.Instance);

    private async Task<NpgsqlDataSource> CreateDatabaseAsync() => NpgsqlDataSource.Create(await postgres.CreateDatabaseAsync());
}
