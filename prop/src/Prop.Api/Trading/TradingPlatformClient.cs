using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

using Prop.Api.Firms;
using Prop.Rules;

namespace Prop.Api.Trading;

/// <summary>
/// Our trading platform's admin API v1 over HTTP. The contract is <c>contracts/trading/trading-service.json</c>,
/// and a test checks that every path and field used here is in it.
/// </summary>
internal sealed class TradingPlatformClient(IHttpClientFactory httpClients) : ITradingPlatform
{
    public const string HttpClientName = "TradingPlatform";

    private const string Admin = "api/admin/v1/";
    private const string ApiKeyHeader = "X-Api-Key";

    public async Task<Guid> EnsureUserAsync(FirmTrading firm, string email, string password, CancellationToken cancellationToken)
    {
        if (await FindUserAsync(firm, email, cancellationToken) is { } existing)
        {
            return existing;
        }

        using var response = await SendAsync(firm, HttpMethod.Post, $"{Admin}users", new { email, password }, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // Created by another request in between.
            return await FindUserAsync(firm, email, cancellationToken)
                ?? throw new TradingPlatformRejectedException($"The trading platform reports {email} as taken but cannot find it.");
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadJsonAsync(response, cancellationToken)).GetProperty("userId").GetGuid();
    }

    public async Task OpenAccountAsync(FirmTrading firm, string accountId, decimal initialBalance, Guid ownerUserId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            firm,
            HttpMethod.Post,
            $"{Admin}accounts",
            new { accountId, groupId = firm.Group, initialBalance, ownerUserId },
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            // A retry after the account was made. It must be the firm's own, or the id belongs to someone else.
            using var existing = await SendAsync(firm, HttpMethod.Get, $"{Admin}accounts/{Uri.EscapeDataString(accountId)}", null, cancellationToken);
            if (existing.IsSuccessStatusCode)
            {
                return;
            }

            throw new TradingPlatformRejectedException($"The trading account id {accountId} is already used outside the firm.");
        }

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SetFloorAsync(FirmTrading firm, string accountId, string floorId, FloorSpec floor, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            firm,
            HttpMethod.Put,
            $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/floors/{Uri.EscapeDataString(floorId)}",
            new JsonObject { ["rule"] = ToTradingRule(floor) },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken, toleratedReason: "AccountDisabled");
    }

    public async Task CloseAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(firm, HttpMethod.Post, $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/close", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken, toleratedReason: "AccountDisabled");
    }

    // The platform answers a repeated suspend or resume with no events, so a retry is done.
    public async Task SuspendAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(firm, HttpMethod.Post, $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/suspend", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken, toleratedReason: "AccountDisabled");
    }

    public async Task ResumeAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(firm, HttpMethod.Post, $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/resume", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken, toleratedReason: "AccountDisabled");
    }

    public async Task ReopenAccountAsync(FirmTrading firm, string accountId, decimal balance, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(firm, HttpMethod.Post, $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/reopen", new { balance }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task WithdrawAsync(FirmTrading firm, string accountId, string operationId, decimal amount, decimal minBalance, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            firm,
            HttpMethod.Post,
            $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/balance-operations",
            new { operationId, amount = -amount, minBalance },
            cancellationToken);

        // A retry after the withdrawal went through: the operation id is already used on the account.
        await EnsureSuccessAsync(response, cancellationToken, toleratedReason: "DuplicateId");
    }

    public async Task DepositAsync(FirmTrading firm, string accountId, string operationId, decimal amount, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            firm, HttpMethod.Post, $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/balance-operations", new { operationId, amount }, cancellationToken);

        // A retry after the deposit went through: the operation id is already used on the account.
        await EnsureSuccessAsync(response, cancellationToken, toleratedReason: "DuplicateId");
    }

    public async Task DescribeAccountAsync(FirmTrading firm, string accountId, TradingAccountDetails details, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            firm,
            HttpMethod.Put,
            $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/details",
            new { label = details.Label, profitTarget = details.ProfitTarget, timeZone = details.TimeZone, detailsUrl = details.DetailsUrl.ToString() },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task SetTradingDayAsync(FirmTrading firm, string accountId, TradingDayDefinition day, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            firm, HttpMethod.Put, $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/trading-day", new { timeZone = day.TimeZone, startsAt = day.Start }, cancellationToken);

        // An ended account has no trading days left to count.
        await EnsureSuccessAsync(response, cancellationToken, toleratedReason: "AccountDisabled");
    }

    public async Task DescribeRulesAsync(FirmTrading firm, string accountId, TradingAccountRules rules, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            firm,
            HttpMethod.Put,
            $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/rules",
            new
            {
                funded = rules.Funded,
                tradingDaysRequired = rules.TradingDaysRequired,
                tradingDaysCounted = rules.TradingDaysCounted,
                passBy = rules.PassBy,
                openPositionBy = rules.OpenPositionBy,
                consistencyPercent = rules.ConsistencyPercent,
                bestDayPercent = rules.BestDayPercent,
            },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<TradingAccountSnapshot?> GetAccountAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(firm, HttpMethod.Get, $"{Admin}accounts/{Uri.EscapeDataString(accountId)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        var account = await ReadJsonAsync(response, cancellationToken);
        return new TradingAccountSnapshot(
            account.GetProperty("balance").GetDecimal(),
            account.GetProperty("equity").GetDecimal(),
            [
                .. account.GetProperty("floors").EnumerateArray().Select(f => new TradingFloorSnapshot(
                    f.GetProperty("floorId").GetString()!,
                    f.GetProperty("level").GetDecimal(),
                    f.GetProperty("headroom").GetDecimal())),
            ],
            account.TryGetProperty("ownLimits", out var own) ? OwnLimitsOf(own) : null);
    }

    private static TradingOwnLimits OwnLimitsOf(JsonElement own) =>
        new(
            AmountsOf(own.GetProperty("limits")),
            own.GetProperty("pending") is { ValueKind: JsonValueKind.Object } pending ? AmountsOf(pending) : null,
            own.GetProperty("tradesToday").GetInt32(),
            own.GetProperty("nextDayStart").GetDateTimeOffset(),
            own.GetProperty("lock") is { ValueKind: JsonValueKind.Object } held
                ? new OwnLock(held.GetProperty("until").GetDateTimeOffset(), Enum.Parse<OwnLockReason>(held.GetProperty("reason").GetString()!))
                : null);

    private static OwnLimitAmounts AmountsOf(JsonElement limits) =>
        new(DecimalOrNull(limits.GetProperty("dailyLoss")), DecimalOrNull(limits.GetProperty("dailyTarget")), IntOrNull(limits.GetProperty("maxTrades")));

    private static decimal? DecimalOrNull(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();

    private static int? IntOrNull(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();

    public async Task<TradeReceipt?> GetReceiptAsync(FirmTrading firm, string accountId, string positionId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            firm,
            HttpMethod.Get,
            $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/positions/{Uri.EscapeDataString(positionId)}/receipt",
            null,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return TradingReportJson.Read<TradeReceipt>(await ReadJsonAsync(response, cancellationToken));
    }

    public async Task<BreachReport?> GetBreachReportAsync(FirmTrading firm, string accountId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(firm, HttpMethod.Get, $"{Admin}accounts/{Uri.EscapeDataString(accountId)}/breach-report", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return TradingReportJson.ReadBreachReport(await ReadJsonAsync(response, cancellationToken));
    }

    public async Task<TradingImpact> GetImpactAsync(FirmTrading firm, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var query = $"{Admin}impact?from={Uri.EscapeDataString(from.ToString("O", CultureInfo.InvariantCulture))}&to={Uri.EscapeDataString(to.ToString("O", CultureInfo.InvariantCulture))}";
        using var response = await SendAsync(firm, HttpMethod.Get, query, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return TradingReportJson.Read<TradingImpact>(await ReadJsonAsync(response, cancellationToken));
    }

    public async Task SetNoticeAsync(FirmTrading firm, TradingNotice? notice, CancellationToken cancellationToken)
    {
        using var response = notice is null
            ? await SendAsync(firm, HttpMethod.Delete, $"{Admin}notice", null, cancellationToken)
            : await SendAsync(
                firm,
                HttpMethod.Put,
                $"{Admin}notice",
                new { title = notice.Title, text = notice.Text, level = notice.Warning ? "Warning" : "Info", url = notice.Url },
                cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<TradingEventPage> ReadEventsAsync(FirmTrading firm, long after, int limit, int waitSeconds, CancellationToken cancellationToken)
    {
        var query = string.Create(CultureInfo.InvariantCulture, $"{Admin}events?after={after}&limit={limit}&wait={waitSeconds}");
        using var response = await SendAsync(firm, HttpMethod.Get, query, null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var page = await ReadJsonAsync(response, cancellationToken);
        return new TradingEventPage(
            [.. page.GetProperty("events").EnumerateArray().Select(ParseEvent)],
            page.GetProperty("cursor").GetInt64());
    }

    public async Task<TradingLoginLink> CreateLoginLinkAsync(FirmTrading firm, Guid userId, string? accountId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(firm, HttpMethod.Post, $"{Admin}users/{userId}/login-links", new { accountId }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var link = await ReadJsonAsync(response, cancellationToken);
        return new TradingLoginLink(new Uri(link.GetProperty("url").GetString()!), link.GetProperty("expiresAt").GetDateTimeOffset());
    }

    public async Task<IReadOnlyList<TradingInstrument>> GetInstrumentsAsync(FirmTrading firm, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(firm, HttpMethod.Get, $"{Admin}instruments", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return
        [
            .. (await ReadJsonAsync(response, cancellationToken)).EnumerateArray().Select(i => new TradingInstrument(
                i.GetProperty("symbol").GetString()!,
                i.GetProperty("baseCurrency").GetString()!,
                i.GetProperty("quoteCurrency").GetString()!,
                i.GetProperty("contractSize").GetDecimal(),
                i.GetProperty("digits").GetInt32())),
        ];
    }

    public async Task<TradingGroupConditions?> GetGroupAsync(FirmTrading firm, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(firm, HttpMethod.Get, $"{Admin}groups", null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        foreach (var group in (await ReadJsonAsync(response, cancellationToken)).EnumerateArray())
        {
            if (group.GetProperty("id").GetString() == firm.Group)
            {
                return new TradingGroupConditions(
                    firm.Group,
                    group.GetProperty("currency").GetString()!,
                    group.GetProperty("changeable").GetBoolean(),
                    [
                        .. group.GetProperty("symbols").EnumerateArray().Select(s => new TradingSymbolConditions(
                            s.GetProperty("symbol").GetString()!,
                            s.GetProperty("leverage").GetInt32(),
                            s.GetProperty("spreadMarkupPoints").GetInt32(),
                            s.GetProperty("commissionPerLotPerSide").GetDecimal())),
                    ]);
            }
        }

        return null;
    }

    public async Task SetGroupSymbolsAsync(FirmTrading firm, IReadOnlyList<TradingSymbolConditions> symbols, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(
            firm,
            HttpMethod.Put,
            $"{Admin}groups/{Uri.EscapeDataString(firm.Group)}/symbols",
            new { symbols = symbols.Select(s => new { symbol = s.Symbol, leverage = s.Leverage, spreadMarkupPoints = s.SpreadMarkupPoints, commissionPerLotPerSide = s.CommissionPerLotPerSide }) },
            cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <summary>Reads one event envelope from the stream. Unknown kinds become <see cref="TradingOtherEvent"/>.</summary>
    internal static TradingEvent ParseEvent(JsonElement envelope)
    {
        var sequence = envelope.GetProperty("sequence").GetInt64();
        var e = envelope.GetProperty("event");
        var time = e.GetProperty("timestamp").GetDateTimeOffset();
        var accountId = e.TryGetProperty("accountId", out var account) && account.ValueKind == JsonValueKind.String ? account.GetString()! : "";
        var raw = e.GetRawText();
        return e.GetProperty("kind").GetString() switch
        {
            "AccountCreated" => new TradingAccountCreated(sequence, time, accountId, raw, e.GetProperty("balance").GetDecimal()),
            "PositionOpened" => new TradingPositionOpened(
                sequence,
                time,
                accountId,
                raw,
                e.GetProperty("positionId").GetString()!,
                e.GetProperty("symbol").GetString()!,
                Enum.Parse<TradeSide>(e.GetProperty("side").GetString()!),
                e.GetProperty("volume").GetDecimal(),
                e.GetProperty("openPrice").GetDecimal(),
                e.GetProperty("commission").GetDecimal(),
                e.GetProperty("balanceAfter").GetDecimal()),
            "PositionClosed" => new TradingPositionClosed(
                sequence,
                time,
                accountId,
                raw,
                e.GetProperty("positionId").GetString()!,
                e.GetProperty("symbol").GetString()!,
                Enum.Parse<TradeSide>(e.GetProperty("side").GetString()!),
                e.GetProperty("volume").GetDecimal(),
                e.GetProperty("openPrice").GetDecimal(),
                e.GetProperty("closePrice").GetDecimal(),
                e.GetProperty("profit").GetDecimal(),
                e.GetProperty("commission").GetDecimal(),
                e.GetProperty("reason").GetString()!,
                e.GetProperty("balanceAfter").GetDecimal()),
            "PositionPartiallyClosed" => new TradingPositionPartiallyClosed(
                sequence,
                time,
                accountId,
                raw,
                e.GetProperty("positionId").GetString()!,
                e.GetProperty("volume").GetDecimal(),
                e.GetProperty("remainingVolume").GetDecimal(),
                e.GetProperty("closePrice").GetDecimal(),
                e.GetProperty("profit").GetDecimal(),
                e.GetProperty("commission").GetDecimal(),
                e.GetProperty("balanceAfter").GetDecimal()),
            "EquityFloorSet" => new TradingFloorSet(sequence, time, accountId, raw, e.GetProperty("floorId").GetString()!, e.GetProperty("level").GetDecimal()),
            "EquityFloorBreached" => new TradingFloorBreached(
                sequence,
                time,
                accountId,
                raw,
                e.GetProperty("floorId").GetString()!,
                e.GetProperty("level").GetDecimal(),
                e.GetProperty("equity").GetDecimal()),
            "AccountDisabled" => new TradingAccountDisabled(sequence, time, accountId, raw),
            "AccountReopened" => new TradingAccountReopened(sequence, time, accountId, raw, e.GetProperty("balance").GetDecimal()),
            "TradingLocked" => new TradingDayLocked(
                sequence,
                time,
                accountId,
                raw,
                Enum.Parse<OwnLockReason>(e.GetProperty("reason").GetString()!),
                e.GetProperty("until").GetDateTimeOffset(),
                DecimalOrNull(e.GetProperty("limit")),
                e.GetProperty("dayResult").GetDecimal(),
                e.GetProperty("positionsClosed").GetInt32()),
            "BalanceAdjusted" => new TradingBalanceAdjusted(
                sequence,
                time,
                accountId,
                raw,
                e.GetProperty("operationId").GetString()!,
                e.GetProperty("amount").GetDecimal(),
                e.GetProperty("balanceAfter").GetDecimal()),
            _ => new TradingOtherEvent(sequence, time, accountId, raw),
        };
    }

    /// <summary>The rule engine's floor as the trading platform's rule.</summary>
    internal static JsonObject ToTradingRule(FloorSpec floor) => floor switch
    {
        StartOfDayFloor start => new JsonObject
        {
            ["kind"] = "AnchoredFloor",
            ["distance"] = start.Distance,
            ["anchor"] = start.Reference == DailyLossReference.Balance ? "Balance" : "HigherOfBalanceAndEquity",
        },
        FixedFloor fixedFloor => new JsonObject { ["kind"] = "FixedFloor", ["level"] = fixedFloor.Level },
        TrailingFloor trailing => new JsonObject { ["kind"] = "TrailingFloor", ["distance"] = trailing.Distance, ["lockLevel"] = trailing.LockLevel },
        _ => throw new ArgumentException($"Unknown floor {floor.GetType().Name}.", nameof(floor)),
    };

    private async Task<HttpResponseMessage> SendAsync(FirmTrading firm, HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
        request.Headers.Add(ApiKeyHeader, firm.ApiKey);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        try
        {
            return await httpClients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new TradingPlatformUnavailableException($"The trading platform could not be reached: {exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TradingPlatformUnavailableException("The trading platform did not answer in time.", exception);
        }
    }

    private async Task<Guid?> FindUserAsync(FirmTrading firm, string email, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(firm, HttpMethod.Get, $"{Admin}users?email={Uri.EscapeDataString(email)}", null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return (await ReadJsonAsync(response, cancellationToken)).GetProperty("userId").GetGuid();
    }

    // Server errors are worth trying again; anything else the platform refused is final.
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken, string? toleratedReason = null)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new TradingPlatformUnavailableException($"The trading platform answered {(int)response.StatusCode}: {body}");
        }

        var reason = ReasonOf(body);
        if (toleratedReason is not null && reason == toleratedReason)
        {
            return;
        }

        throw new TradingPlatformRejectedException(
            $"The trading platform refused {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}: {(int)response.StatusCode} {body}",
            reason);
    }

    private static string? ReasonOf(string problem)
    {
        try
        {
            using var document = JsonDocument.Parse(problem);
            return document.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return document.RootElement.Clone();
    }
}
