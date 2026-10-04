using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Prop.Api.Tests.Support;

namespace Prop.Api.Tests;

/// <summary>Discount codes in the firm's shop, codes for a new try after a failed challenge, and what a breach closed.</summary>
public sealed class DiscountTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Challenge = "two-step-100k";
    private const string Phase1 = "demo-firm-1001-1";

    [Fact]
    public async Task TheShopTakesACodeOffThePriceAndTheOrderKeepsBoth()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();
        using var buyer = factory.CreatePortalClient();
        var code = await CreateCodeAsync(admin, new { code = "spring20", percentOff = 20m, forRetries = false });

        var quote = await QuoteAsync(buyer, " SPRING20 ");
        var (orderId, token) = await BuyAsync(buyer, "buyer@test.example", "Spring20");
        var order = await buyer.GetFromJsonAsync<JsonElement>(Url($"orders/{orderId}?token={token}"), TestContext.Current.CancellationToken);
        var codes = await admin.GetFromJsonAsync<JsonElement>(Url("admin/discounts"), TestContext.Current.CancellationToken);
        using var deleted = await admin.DeleteAsync(Url($"admin/discounts/{code}"), TestContext.Current.CancellationToken);
        using var turnedOff = await admin.PutAsJsonAsync(Url($"admin/discounts/{code}/active"), new { active = false }, TestContext.Current.CancellationToken);
        using var ended = await buyer.PostAsJsonAsync(Url("shop/discount"), new { code = "spring20", challengeId = Challenge }, TestContext.Current.CancellationToken);

        Assert.Equal(("spring20", 499m, 99.80m, 399.20m), (quote.GetProperty("code").GetString(), quote.GetProperty("listAmount").GetDecimal(), quote.GetProperty("discount").GetDecimal(), quote.GetProperty("amount").GetDecimal()));
        Assert.Equal((399.20m, 499m, "spring20"), (order.GetProperty("amount").GetDecimal(), order.GetProperty("listAmount").GetDecimal(), order.GetProperty("discountCode").GetString()));
        Assert.Equal(1, Assert.Single(codes.EnumerateArray()).GetProperty("uses").GetInt32());
        Assert.Equal((HttpStatusCode.Conflict, HttpStatusCode.NoContent), (deleted.StatusCode, turnedOff.StatusCode));
        Assert.Equal("That code has ended.", await TitleOfAsync(ended));
    }

    [Fact]
    public async Task ACodeIsNotUsedMoreOftenThanTheFirmAllows()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();
        using var buyer = factory.CreatePortalClient();
        await CreateCodeAsync(admin, new { code = "ONCE", amountOff = 50m, currency = "USD", maxUses = 1, challengeIds = new[] { Challenge } });

        await BuyAsync(buyer, "first@test.example", "ONCE");
        using var second = await buyer.PostAsJsonAsync(
            Url("orders"),
            new { challengeId = Challenge, email = "second@test.example", acceptTerms = true, name = "Ann Buyer", country = "SE", discountCode = "ONCE" },
            TestContext.Current.CancellationToken);
        using var otherChallenge = await buyer.PostAsJsonAsync(Url("shop/discount"), new { code = "ONCE", challengeId = "quick-test-100k" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("That code has been used up.", await TitleOfAsync(second));
        Assert.Equal("That code is not for this challenge.", await TitleOfAsync(otherChallenge));
    }

    [Theory]
    [InlineData("two words", 10, null, null, "The code is 1 to 32 letters, digits, - or _.")]
    [InlineData("ZERO", 0, null, null, "The percentage off must be 1 to 99, with at most two decimals.")]
    [InlineData("BOTH", 10, 5, "USD", "Give either a percentage or an amount off.")]
    [InlineData("NOCURRENCY", null, 5, null, "Choose the currency of the amount off.")]
    public async Task ACodeMustBeValid(string code, int? percentOff, int? amountOff, string? currency, string problem)
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();

        using var response = await admin.PostAsJsonAsync(Url("admin/discounts"), new { code, percentOff, amountOff, currency }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(problem, await TitleOfAsync(response));
    }

    [Fact]
    public async Task TheSameCodeCannotBeMadeTwiceAndAnAmountOnlyFitsItsCurrency()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();
        using var buyer = factory.CreatePortalClient();
        await CreateCodeAsync(admin, new { code = "EURO", amountOff = 10m, currency = "EUR" });

        using var again = await admin.PostAsJsonAsync(Url("admin/discounts"), new { code = "euro", percentOff = 5m }, TestContext.Current.CancellationToken);
        using var quote = await buyer.PostAsJsonAsync(Url("shop/discount"), new { code = "EURO", challengeId = Challenge }, TestContext.Current.CancellationToken);
        using var unknown = await buyer.PostAsJsonAsync(Url("shop/discount"), new { code = "NOPE", challengeId = Challenge }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("That code is only for prices in EUR.", await TitleOfAsync(quote));
        Assert.Equal("There is no such code.", await TitleOfAsync(unknown));
    }

    [Fact]
    public async Task AFailedChallengeOffersANewTryWithTheCodeForRetriesAndSaysWhatTheBreachClosed()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        using var admin = await factory.LogInAsAdminAsync();
        await CreateCodeAsync(admin, new { code = "COMEBACK", percentOff = 30m, forRetries = true });
        await CreateCodeAsync(admin, new { code = "SMALL", percentOff = 5m, forRetries = true });
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(id);
        using var stranger = factory.CreatePortalClient();

        // The breach closes the position at the next price, which is lower, and charges the commission for closing it.
        factory.Trading.OpenPosition(Phase1, commission: 3.5m);
        factory.Trading.ClosePosition(Phase1, -5_140m, closePrice: 1.0486m, commission: 3.5m, reason: "EquityFloor");
        factory.Trading.Breach(Phase1, "daily", 94_900m);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "Failed");
        JsonElement details = default;
        await Eventually.ThatAsync(
            async () =>
            {
                details = await trader.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}"), TestContext.Current.CancellationToken);
                return details.GetProperty("breach").GetProperty("closes").GetArrayLength() == 1;
            },
            "the breach's closes in the history");
        using var notTheirs = await stranger.PostAsJsonAsync(
            Url("orders"),
            new { challengeId = Challenge, email = "someone@test.example", acceptTerms = true, name = "Some One", country = "SE", discountCode = "COMEBACK" },
            TestContext.Current.CancellationToken);
        var quote = await QuoteAsync(trader, "COMEBACK");

        var retry = details.GetProperty("retry");
        Assert.Equal((Challenge, 499m, "COMEBACK", 349.30m), (retry.GetProperty("challengeId").GetString(), retry.GetProperty("price").GetDecimal(), retry.GetProperty("discountCode").GetString(), retry.GetProperty("amount").GetDecimal()));
        var close = Assert.Single(details.GetProperty("breach").GetProperty("closes").EnumerateArray());
        Assert.Equal((1.0486m, -5_140m, 3.5m), (close.GetProperty("closePrice").GetDecimal(), close.GetProperty("profit").GetDecimal(), close.GetProperty("commission").GetDecimal()));
        Assert.Equal(94_853m, details.GetProperty("breach").GetProperty("balanceAfter").GetDecimal());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, notTheirs.StatusCode);
        Assert.Equal("That code is for a new try after a challenge that failed, with the email it was bought with.", await TitleOfAsync(notTheirs));
        Assert.Equal((349.30m, true), (quote.GetProperty("amount").GetDecimal(), quote.GetProperty("forRetries").GetBoolean()));
    }

    [Fact]
    public async Task AnActiveAccountOffersNoNewTry()
    {
        await using var factory = PropFactory.Create(await postgres.CreateDatabaseAsync());
        var id = (await factory.StartActiveAccountAsync()).GetProperty("id").GetGuid();
        using var trader = await factory.LogInAsTraderAsync(id);

        var details = await trader.GetFromJsonAsync<JsonElement>(Url($"accounts/{id}"), TestContext.Current.CancellationToken);

        Assert.Equal(JsonValueKind.Null, details.GetProperty("retry").ValueKind);
    }

    private static Uri Url(string path) => new($"/api/portal/{path}", UriKind.Relative);

    private static async Task<string> TitleOfAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("title").GetString()!;

    private static async Task<Guid> CreateCodeAsync(HttpClient admin, object code)
    {
        using var response = await admin.PostAsJsonAsync(Url("admin/discounts"), code, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> QuoteAsync(HttpClient buyer, string code)
    {
        using var response = await buyer.PostAsJsonAsync(Url("shop/discount"), new { code, challengeId = Challenge }, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private static async Task<(Guid OrderId, string Token)> BuyAsync(HttpClient buyer, string email, string discountCode)
    {
        using var response = await buyer.PostAsJsonAsync(
            Url("orders"),
            new { challengeId = Challenge, email, acceptTerms = true, name = "Ann Buyer", country = "SE", discountCode },
            TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var checkoutUrl = created.GetProperty("checkoutUrl").GetString()!;
        return (created.GetProperty("orderId").GetGuid(), checkoutUrl.Split("token=")[1].Split('&')[0]);
    }
}
