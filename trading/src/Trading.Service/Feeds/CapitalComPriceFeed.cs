using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;

using Trading.Service.Candles;
using Trading.Service.Configuration;

namespace Trading.Service.Feeds;

/// <summary>Capital.com's API through a free demo account. The key and password belong in user secrets, never in files.</summary>
public sealed class CapitalComOptions
{
    /// <summary>Made under Settings, API integrations on Capital.com.</summary>
    public string ApiKey { get; init; } = "";

    /// <summary>The email the Capital.com account logs in with.</summary>
    public string Identifier { get; init; } = "";

    /// <summary>The password chosen for the API key.</summary>
    public string Password { get; init; } = "";

    /// <summary>At most one price per symbol and interval reaches the engine, always the latest.</summary>
    public TimeSpan QuoteInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>A session ends after 10 minutes without use, so the stream is pinged more often than that.</summary>
    public TimeSpan PingInterval { get; init; } = TimeSpan.FromMinutes(4);

    /// <summary>Capital.com answers every ping, so silence for longer than this means the stream is dead.</summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(6);

    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Capital.com allows 10 calls a second, so the history starts its calls at least this far apart.</summary>
    public TimeSpan HistoryCallInterval { get; init; } = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Capital.com allows one new session a second, and the stream and the history each open one, for example when a
    /// gap in the charts is filled right after the stream connects (ADR 0056). New sessions start at least this far apart.
    /// </summary>
    public TimeSpan SessionInterval { get; init; } = TimeSpan.FromSeconds(1.2);

    /// <summary>The demo environment. A login with only a live account uses https://api-capital.backend-capital.com/, read only.</summary>
    public Uri ApiUrl { get; init; } = new("https://demo-api-capital.backend-capital.com/");

    public Uri StreamUrl { get; init; } = new("wss://api-streaming-capital.backend-capital.com/connect");

    /// <summary>Capital.com's names (epics) for the symbols whose names differ there. Other symbols keep their own.</summary>
    public Dictionary<string, string> Epics { get; init; } = new(StringComparer.Ordinal)
    {
        ["XAUUSD"] = "GOLD",
        ["XAGUSD"] = "SILVER",
        ["JP225"] = "J225",
        ["USOIL"] = "OIL_CRUDE",
        ["UKOIL"] = "OIL_BRENT",
        ["NATGAS"] = "NATURALGAS",
    };
}

/// <summary>
/// Live prices for forex, metals, indices, commodities and crypto from Capital.com's streaming API, for development.
/// Every connection opens a session, fetches the latest prices and subscribes to every instrument. The stream is
/// pinged so the session stays open, and reconnects with backoff when it fails or goes silent. Prices are rounded
/// outwards to the instrument's digits (bid down, ask up). The history comes from Capital.com's bars of bid prices.
/// <para>The demo account is free, but its prices are for our own development and may not be shown to others.</para>
/// </summary>
internal sealed partial class CapitalComPriceFeed : IPriceFeed
{
    public const string HttpClientName = "CapitalCom";

    /// <summary>Capital.com streams at most this many instruments on one connection.</summary>
    public const int MaxInstruments = 40;

    private const int MaxMessageBytes = 1 << 20;

    // Capital.com answers at most 1 000 bars a call and refuses a period that holds that many, so calls ask for fewer.
    private const int MaxBarsPerCall = 1_000;
    private const int BarsPerCall = 950;

    private readonly IHttpClientFactory _http;
    private readonly CapitalComOptions _options;
    private readonly Lock _sessionLock = new();

    // When the next new session may start, as a Stopwatch timestamp.
    private long _nextSessionAt;
    private readonly ILogger<CapitalComPriceFeed> _logger;
    private readonly Dictionary<string, (string Symbol, int Digits)> _byEpic;
    private readonly string[] _epics;

    public CapitalComPriceFeed(IHttpClientFactory http, IOptions<PriceFeedOptions> options, IOptions<TradingOptions> trading, ILogger<CapitalComPriceFeed> logger)
    {
        _http = http;
        _options = options.Value.CapitalCom;
        _logger = logger;
        _byEpic = trading.Value.Instruments.ToDictionary(
            i => _options.Epics.GetValueOrDefault(i.Symbol, i.Symbol),
            i => (i.Symbol, i.Digits),
            StringComparer.Ordinal);
        _epics = [.. _byEpic.Keys.Order(StringComparer.Ordinal)];
        if (_epics.Length > MaxInstruments)
        {
            throw new InvalidOperationException($"Capital.com streams at most {MaxInstruments} instruments, and {_epics.Length} are configured.");
        }
    }

    public string Name => PriceFeedOptions.CapitalComProvider;

    public bool FollowsTradingHours => true;

    /// <summary>Capital.com's bars of bid prices, rounded down to the instrument's digits. Calls are spaced to stay within Capital.com's limit.</summary>
    public async Task<IReadOnlyList<ChartBar>> GetHistoryAsync(IReadOnlyList<HistorySpan> spans, CancellationToken cancellationToken)
    {
        var session = await CreateSessionAsync(cancellationToken);
        var calls = 1;
        var lastCall = 0L;
        var bars = new List<ChartBar>();
        foreach (var epic in _epics)
        {
            var (symbol, digits) = _byEpic[epic];
            foreach (var span in spans)
            {
                var length = CandleStore.Duration(span.Resolution);
                var window = length * BarsPerCall;
                for (var from = span.From; from < span.Until; from += window)
                {
                    var until = from + window < span.Until ? from + window : span.Until;

                    // Calls start at most once an interval, so slow answers need no extra wait.
                    var wait = _options.HistoryCallInterval - Stopwatch.GetElapsedTime(lastCall);
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, cancellationToken);
                    }

                    lastCall = Stopwatch.GetTimestamp();
                    calls++;
                    using var document = await GetAsync(
                        session,
                        string.Create(
                            CultureInfo.InvariantCulture,
                            $"api/v1/prices/{epic}?resolution={Resolution(span.Resolution)}&max={MaxBarsPerCall}&from={from.UtcDateTime:yyyy-MM-ddTHH:mm:ss}&to={until.UtcDateTime:yyyy-MM-ddTHH:mm:ss}"),
                        cancellationToken);

                    // Not found means no bars in the period, for example a weekend.
                    if (document is null)
                    {
                        continue;
                    }

                    bars.AddRange(Property(document.RootElement, "prices").EnumerateArrayOrEmpty()
                        .Select(item => ToBar(item, symbol, digits, span.Resolution))
                        .OfType<ChartBar>()
                        .Where(b => b.Candle.Time >= from && b.Candle.Time < until && b.Candle.Time + length <= span.Until));
                }
            }
        }

        var history = HistoryBars.LeaveOutUnmoved(bars).ToList();
        LogHistoryFetched(_logger, history.Count, calls);
        return history;
    }

    public async IAsyncEnumerable<FeedQuote> StreamAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var latest = new LatestQuotes();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var receiving = Task.Run(() => KeepStreamingAsync(latest, stop.Token), CancellationToken.None);
        try
        {
            using var timer = new PeriodicTimer(_options.QuoteInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                foreach (var quote in latest.TakeAll())
                {
                    yield return quote;
                }
            }
        }
        finally
        {
            await stop.CancelAsync();
            await receiving;
        }
    }

    /// <summary>
    /// Parses one message of the stream. Returns null for confirmations, ping answers, unknown instruments and
    /// incomplete prices. Throws when Capital.com reports an error, for example a session that has ended.
    /// </summary>
    public FeedQuote? Parse(string message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        var status = Property(root, "status");
        var destination = Property(root, "destination");
        if (status.ValueKind == JsonValueKind.String && !status.ValueEquals("OK"))
        {
            throw new InvalidOperationException($"Capital.com refused {destination}: {Property(Property(root, "payload"), "errorCode")}");
        }

        if (destination.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var payload = Property(root, "payload");
        if (destination.ValueEquals("marketData.subscribe"))
        {
            LogRefusedSubscriptions(payload);
            return null;
        }

        return destination.ValueEquals("quote")
            ? ToQuote(Property(payload, "epic"), Property(payload, "bid"), Property(payload, "ofr"), Property(payload, "timestamp"))
            : null;
    }

    private static string Resolution(Timeframe resolution) => resolution switch
    {
        Timeframe.M1 => "MINUTE",
        Timeframe.M5 => "MINUTE_5",
        Timeframe.M15 => "MINUTE_15",
        Timeframe.M30 => "MINUTE_30",
        Timeframe.H1 => "HOUR",
        Timeframe.H4 => "HOUR_4",
        Timeframe.D1 => "DAY",
        _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, null),
    };

    /// <summary>What to do about the errors a new developer is likely to meet.</summary>
    private static string SessionHint(string errorCode) => errorCode switch
    {
        "error.null.accountId" => ". The login has no trading account yet: open a demo account in Capital.com's platform.",
        "error.invalid.details" => ". Check PriceFeed:CapitalCom:Identifier (the login email) and Password (the API key's own password).",
        "error.invalid.api.key" => ". Check PriceFeed:CapitalCom:ApiKey.",
        _ => "",
    };

    private static bool TryGetPrice(JsonElement element, out decimal price)
    {
        price = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out price);
    }

    /// <summary>The property, or an undefined element when it is missing.</summary>
    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static async Task SendAsync(ClientWebSocket socket, object message, CancellationToken cancellationToken) =>
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    /// <summary>Reads one whole message. Returns null when Capital.com closes the connection.</summary>
    private static async Task<string?> ReceiveAsync(ClientWebSocket socket, ArrayBufferWriter<byte> buffer, CancellationToken cancellationToken)
    {
        buffer.ResetWrittenCount();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.GetMemory(4096), cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                return null;
            }

            buffer.Advance(result.Count);
            if (buffer.WrittenCount > MaxMessageBytes)
            {
                throw new InvalidDataException("A message from Capital.com is too large.");
            }

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(buffer.WrittenSpan);
            }
        }
    }

    private async Task KeepStreamingAsync(LatestQuotes latest, CancellationToken cancellationToken)
    {
        var delay = _options.ReconnectDelay;
        try
        {
            while (true)
            {
                try
                {
                    await StreamOnceAsync(latest, () => delay = _options.ReconnectDelay, cancellationToken);
                    LogStreamEnded(_logger, delay);
                }
                catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
                {
                    // Every failure is retried: a broken price source must never stop the service.
                    LogStreamFailed(_logger, delay, exception);
                }

                await Task.Delay(delay, cancellationToken);
                delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxReconnectDelay.Ticks));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task StreamOnceAsync(LatestQuotes latest, Action onSubscribed, CancellationToken cancellationToken)
    {
        var session = await CreateSessionAsync(cancellationToken);
        await LoadLatestPricesAsync(session, latest, cancellationToken);

        using var socket = new ClientWebSocket();
        // Pings find connections that died without being closed.
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);

        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var pinging = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task? pings = null;
        try
        {
            idle.CancelAfter(_options.IdleTimeout);
            await socket.ConnectAsync(_options.StreamUrl, idle.Token);
            await SendAsync(socket, Message("marketData.subscribe", session, new { epics = _epics }), idle.Token);

            // The only sender from here on, since a WebSocket takes one send at a time.
            pings = PingAsync(socket, session, pinging.Token);

            var buffer = new ArrayBufferWriter<byte>();
            var subscribed = false;
            while (true)
            {
                idle.CancelAfter(_options.IdleTimeout);
                if (await ReceiveAsync(socket, buffer, idle.Token) is not { } message)
                {
                    return;
                }

                var quote = Parse(message);
                if (!subscribed)
                {
                    // The first message that is not an error confirms the subscription.
                    subscribed = true;
                    onSubscribed();
                    LogSubscribed(_logger, _epics.Length);
                }

                if (quote is not null)
                {
                    latest.Set(quote);
                }
            }
        }
        catch (OperationCanceledException) when (idle.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Capital.com sent nothing for {_options.IdleTimeout}.");
        }
        finally
        {
            await pinging.CancelAsync();
            if (pings is not null)
            {
                await pings;
            }
        }
    }

    private async Task PingAsync(ClientWebSocket socket, Session session, CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await Task.Delay(_options.PingInterval, cancellationToken);
                await SendAsync(socket, Message("ping", session, new { }), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // The stream has ended. The receiving loop reconnects.
        }
    }

    private static object Message(string destination, Session session, object payload) =>
        new { destination, correlationId = Guid.NewGuid().ToString("N"), cst = session.Cst, securityToken = session.SecurityToken, payload };

    /// <summary>A session with the API key, the account's email and the key's password.</summary>
    private async Task<Session> CreateSessionAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait;
        lock (_sessionLock)
        {
            var now = Stopwatch.GetTimestamp();
            var at = Math.Max(now, _nextSessionAt);
            _nextSessionAt = at + (long)(_options.SessionInterval.TotalSeconds * Stopwatch.Frequency);
            wait = Stopwatch.GetElapsedTime(now, at);
        }

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.ApiUrl, "api/v1/session"))
        {
            Content = JsonContent.Create(new { identifier = _options.Identifier, password = _options.Password, encryptedPassword = false }),
        };
        request.Headers.Add("X-CAP-API-KEY", _options.ApiKey);
        using var client = _http.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorCode = await ErrorCodeAsync(response, cancellationToken);
            throw new InvalidOperationException($"Capital.com refused the session: {(int)response.StatusCode} {errorCode}{SessionHint(errorCode)}");
        }

        return response.Headers.TryGetValues("CST", out var cst) && response.Headers.TryGetValues("X-SECURITY-TOKEN", out var token)
            ? new Session(cst.First(), token.First())
            : throw new InvalidOperationException("Capital.com's session has no CST or X-SECURITY-TOKEN header.");
    }

    /// <summary>The answer to an authorised call, or null when it is not found.</summary>
    private async Task<JsonDocument?> GetAsync(Session session, string pathAndQuery, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.ApiUrl, pathAndQuery));
        request.Headers.Add("X-CAP-API-KEY", _options.ApiKey);
        request.Headers.Add("CST", session.Cst);
        request.Headers.Add("X-SECURITY-TOKEN", session.SecurityToken);
        using var client = _http.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Capital.com answered {(int)response.StatusCode} {await ErrorCodeAsync(response, cancellationToken)}",
                inner: null,
                response.StatusCode);
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
    }

    /// <summary>Without these, nothing would be priced until the market moves.</summary>
    private async Task LoadLatestPricesAsync(Session session, LatestQuotes latest, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await GetAsync(session, $"api/v1/markets?epics={string.Join(',', _epics)}", cancellationToken);
            var count = 0;

            // The markets come as details with an instrument and a snapshot, or as plain market summaries.
            var root = document?.RootElement ?? default;
            var details = Property(root, "marketDetails").EnumerateArrayOrEmpty()
                .Select(d => (Epic: Property(Property(d, "instrument"), "epic"), Snapshot: Property(d, "snapshot")));
            var summaries = Property(root, "markets").EnumerateArrayOrEmpty().Select(m => (Epic: Property(m, "epic"), Snapshot: m));
            foreach (var (epic, snapshot) in details.Concat(summaries))
            {
                if (ToQuote(epic, Property(snapshot, "bid"), Property(snapshot, "offer"), default) is { } quote)
                {
                    latest.Set(quote);
                    count++;
                }
            }

            LogLatestPricesLoaded(_logger, count, _epics.Length);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The stream works without them, so this is no reason to fail.
            LogLatestPricesFailed(_logger, exception);
        }
    }

    private FeedQuote? ToQuote(JsonElement epic, JsonElement bid, JsonElement ask, JsonElement timestamp)
    {
        if (epic.ValueKind != JsonValueKind.String
            || !_byEpic.TryGetValue(epic.GetString()!, out var instrument)
            || !TryGetPrice(bid, out var bidPrice)
            || !TryGetPrice(ask, out var askPrice)
            // An empty or crossed book is no price.
            || bidPrice <= 0
            || askPrice < bidPrice)
        {
            return null;
        }

        var time = timestamp.ValueKind == JsonValueKind.Number && timestamp.TryGetInt64(out var milliseconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)
            : DateTimeOffset.UtcNow;
        return new FeedQuote(
            instrument.Symbol,
            decimal.Round(bidPrice, instrument.Digits, MidpointRounding.ToNegativeInfinity),
            decimal.Round(askPrice, instrument.Digits, MidpointRounding.ToPositiveInfinity),
            time);
    }

    /// <summary>A bar of bid prices, or null for an incomplete bar.</summary>
    private static ChartBar? ToBar(JsonElement item, string symbol, int digits, Timeframe resolution)
    {
        if (Property(item, "snapshotTimeUTC") is not { ValueKind: JsonValueKind.String } snapshotTime
            || !DateTimeOffset.TryParse(snapshotTime.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var time)
            || !TryGetPrice(Property(Property(item, "openPrice"), "bid"), out var open)
            || !TryGetPrice(Property(Property(item, "highPrice"), "bid"), out var high)
            || !TryGetPrice(Property(Property(item, "lowPrice"), "bid"), out var low)
            || !TryGetPrice(Property(Property(item, "closePrice"), "bid"), out var close))
        {
            return null;
        }

        decimal Bid(decimal price) => decimal.Round(price, digits, MidpointRounding.ToNegativeInfinity);
        return new ChartBar(symbol, resolution, new Candle(time.ToUniversalTime(), Bid(open), Bid(high), Bid(low), Bid(close), 0));
    }

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return Property(document.RootElement, "errorCode").ToString();
        }
        catch (JsonException)
        {
            return "";
        }
    }

    private void LogRefusedSubscriptions(JsonElement payload)
    {
        if (Property(payload, "subscriptions") is { ValueKind: JsonValueKind.Object } subscriptions)
        {
            foreach (var subscription in subscriptions.EnumerateObject().Where(s => s.Value.ValueKind != JsonValueKind.String || !s.Value.ValueEquals("PROCESSED")))
            {
                LogSubscriptionRefused(_logger, subscription.Name, subscription.Value.ToString());
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribed to Capital.com prices for {Count} instruments")]
    private static partial void LogSubscribed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Capital.com refused the prices of {Epic}: {Reason}")]
    private static partial void LogSubscriptionRefused(ILogger logger, string epic, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded the latest Capital.com prices for {Count} of {Total} instruments")]
    private static partial void LogLatestPricesLoaded(ILogger logger, int count, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the latest Capital.com prices, continuing with the stream")]
    private static partial void LogLatestPricesFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Information, Message = "Fetched {Count} bars of Capital.com history in {Calls} calls")]
    private static partial void LogHistoryFetched(ILogger logger, int count, int calls);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Capital.com stream ended, reconnecting in {Delay}")]
    private static partial void LogStreamEnded(ILogger logger, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Capital.com stream failed, reconnecting in {Delay}")]
    private static partial void LogStreamFailed(ILogger logger, TimeSpan delay, Exception exception);

    private sealed record Session(string Cst, string SecurityToken);
}

internal static class JsonElementExtensions
{
    /// <summary>The items of an array, or none when the element is not one.</summary>
    public static IEnumerable<JsonElement> EnumerateArrayOrEmpty(this JsonElement element) =>
        element.ValueKind == JsonValueKind.Array ? element.EnumerateArray() : [];
}
