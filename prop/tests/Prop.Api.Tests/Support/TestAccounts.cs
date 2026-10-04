using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Prop.Api.Tests.Support;

/// <summary>Challenge accounts of the development firm in the state a test needs, with the development challenge quick-test-100k.</summary>
internal static class TestAccounts
{
    public const string QuickTest = "quick-test-100k";

    /// <summary>
    /// Starts the quick challenge for the email, passes both evaluation stages, approves funding and closes a position with
    /// <paramref name="profit"/> on the funded account. The account's number names its trading accounts.
    /// </summary>
    public static async Task<Guid> FundedAsync(PropFactory factory, string email, int number, decimal profit)
    {
        var id = (await factory.StartActiveAccountAsync(email, QuickTest)).GetProperty("id").GetGuid();
        for (var stage = 0; stage < 2; stage++)
        {
            var current = stage;
            await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() == current && a.GetProperty("status").GetString() == "Active");
            factory.Trading.OpenPosition($"demo-firm-{number}-{stage + 1}");
            factory.Trading.ClosePosition($"demo-firm-{number}-{stage + 1}", 100m);
            await factory.WaitForAccountAsync(id, a => a.GetProperty("stage").GetInt32() > current || a.GetProperty("status").GetString() == "AwaitingFunding");
        }

        await factory.WaitForAccountAsync(id, a => a.GetProperty("status").GetString() == "AwaitingFunding");
        using var firm = factory.CreateFirmClient();
        await firm.PostJsonAsync($"accounts/{id}/approve-funding", null);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("funded").GetBoolean() && a.GetProperty("status").GetString() == "Active");
        factory.Trading.OpenPosition($"demo-firm-{number}-3");
        factory.Trading.ClosePosition($"demo-firm-{number}-3", profit);
        await factory.WaitForAccountAsync(id, a => a.GetProperty("balance").GetDecimal() == 100_000m + profit);
        return id;
    }

    /// <summary>The trader asks for a payout of the funded account's profit, which waits for the firm. Returns the payout's id.</summary>
    public static async Task<Guid> RequestPayoutAsync(PropFactory factory, Guid accountId)
    {
        using var firm = factory.CreateFirmClient();
        var payoutId = (await firm.PostJsonAsync($"accounts/{accountId}/payouts", null, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await Eventually.ThatAsync(
            async () => (await firm.GetFromJsonAsync<JsonElement>(new Uri($"/api/firm/v1/payouts/{payoutId}", UriKind.Relative))).GetProperty("status").GetString() == "Pending",
            $"payout {payoutId} to wait for the firm");
        return payoutId;
    }
}
