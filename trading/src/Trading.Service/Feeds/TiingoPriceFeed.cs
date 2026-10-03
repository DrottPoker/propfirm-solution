using System.Buffers;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using Microsoft.Extensions.Options;

using Trading.Service.Configuration;

namespace Trading.Service.Feeds;

/// <summary>Tiingo's forex stream. The API key belongs in user secrets or environment variables, never in files.</summary>
public sealed class TiingoOptions
{
    public string ApiKey { get; init; } = "";

    /// <summary>At most one price per symbol and interval reaches the engine, always the latest.</summary>
    public TimeSpan QuoteInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Tiingo sends a heartbeat every 2 minutes, also while the market is closed. Silence for longer than this
    /// means the subscription is dead. A dead connection is found sooner by the WebSocket pings.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan ReconnectDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Every connection also fetches the latest prices, and the free plan allows 50 such calls an hour.</summary>
    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromMinutes(2);

    public Uri StreamUrl { get; init; } = new("wss://api.tiingo.com/fx");

    public Uri ApiUrl { get; init; } = new("https://api.tiingo.com/");
}

/// <summary>
/// Live forex and gold prices from Tiingo's top-of-book stream. Every connection first fetches the latest
/// prices, since the stream only sends changes and is quiet while the market is closed. Reconnects with
/// backoff when the stream fails or goes silent. Prices are rounded outwards to the instrument's digits
/// (bid down, ask up), so the spread is never made smaller than Tiingo's.
/// <para>Tiingo's free plan does not allow showing the prices to other people. Use it for development only.</para>
/// </summary>
internal sealed partial class TiingoPriceFeed : IPriceFeed
{
    public const string HttpClientName = "Tiingo";

    private const int MaxMessageBytes = 1 << 20;

    private readonly IHttpClientFactory _http;
    private readonly TiingoOptions _options;
    private readonly ILogger<TiingoPriceFeed> _logger;
    private readonly Dictionary<string, (string Symbol, int Digits)> _instruments;
    private readonly string[] _tickers;

    public TiingoPriceFeed(IHttpClientFactory http, IOptions<PriceFeedOptions> options, IOptions<TradingOptions> trading, ILogger<TiingoPriceFeed> logger)
    {
        _http = http;
        _options = options.Value.Tiingo;
        _logger = logger;

        // Tiingo names forex tickers in lower case, for example eurusd and xauusd.
        _instruments = trading.Value.Instruments.ToDictionary(
            i => i.Symbol.ToLowerInvariant(),
            i => (i.Symbol, i.Digits),
            StringComparer.OrdinalIgnoreCase);
        _tickers = [.. _instruments.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>No history: the charts fill from live prices and, after a restart, from the journal.</summary>
    public IEnumerable<FeedQuote> GetBackfill(DateTimeOffset until) => [];

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
    /// Parses one message of the stream. Returns null for subscription confirmations, heartbeats, unknown
    /// tickers and incomplete quotes. Throws when Tiingo reports an error, for example a wrong API key.
    /// </summary>
    public FeedQuote? Parse(string message)
    {
        using var document = JsonDocument.Parse(message);
        var root = document.RootElement;
        var type = Property(root, "messageType");
        if (type.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        if (type.ValueEquals("E"))
        {
            var response = Property(root, "response");
            throw new InvalidOperationException($"Tiingo refused the stream: {Property(response, "code")} {Property(response, "message")}");
        }

        // A quote is ["Q", ticker, time, bidSize, bidPrice, midPrice, askSize, askPrice].
        var data = Property(root, "data");
        if (!type.ValueEquals("A") || data.ValueKind != JsonValueKind.Array || data.GetArrayLength() < 8
            || data[0].ValueKind != JsonValueKind.String || !data[0].ValueEquals("Q"))
        {
            return null;
        }

        return ToQuote(data[1], data[2], data[4], data[7]);
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
        await LoadLatestPricesAsync(latest, cancellationToken);

        using var socket = new ClientWebSocket();
        // Pings find connections that died without being closed.
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);

        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            idle.CancelAfter(_options.IdleTimeout);
            await socket.ConnectAsync(_options.StreamUrl, idle.Token);
            await socket.SendAsync(SubscribeMessage(), WebSocketMessageType.Text, endOfMessage: true, idle.Token);

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
                    LogSubscribed(_logger, _tickers.Length);
                }

                if (quote is not null)
                {
                    latest.Set(quote);
                }
            }
        }
        catch (OperationCanceledException) when (idle.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Tiingo sent nothing for {_options.IdleTimeout}.");
        }
    }

    /// <summary>Without these, nothing would be priced until the market moves.</summary>
    private async Task LoadLatestPricesAsync(LatestQuotes latest, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_options.ApiUrl, $"tiingo/fx/top?tickers={string.Join(',', _tickers)}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Token", _options.ApiKey);
            using var client = _http.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            var count = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (ToQuote(Property(item, "ticker"), Property(item, "quoteTimestamp"), Property(item, "bidPrice"), Property(item, "askPrice")) is { } quote)
                {
                    latest.Set(quote);
                    count++;
                }
            }

            LogLatestPricesLoaded(_logger, count, _tickers.Length);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The stream works without them, so this is no reason to fail.
            LogLatestPricesFailed(_logger, exception);
        }
    }

    private FeedQuote? ToQuote(JsonElement ticker, JsonElement time, JsonElement bid, JsonElement ask)
    {
        if (ticker.ValueKind != JsonValueKind.String
            || !_instruments.TryGetValue(ticker.GetString()!, out var instrument)
            || time.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var timestamp)
            || !TryGetPrice(bid, out var bidPrice)
            || !TryGetPrice(ask, out var askPrice)
            // An empty or crossed book is no price.
            || bidPrice <= 0
            || askPrice < bidPrice)
        {
            return null;
        }

        return new FeedQuote(
            instrument.Symbol,
            decimal.Round(bidPrice, instrument.Digits, MidpointRounding.ToNegativeInfinity),
            decimal.Round(askPrice, instrument.Digits, MidpointRounding.ToPositiveInfinity),
            timestamp);
    }

    private static bool TryGetPrice(JsonElement element, out decimal price)
    {
        price = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out price);
    }

    /// <summary>The property, or an undefined element when it is missing.</summary>
    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private ReadOnlyMemory<byte> SubscribeMessage()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("eventName", "subscribe");
            json.WriteString("authorization", _options.ApiKey);
            json.WriteStartObject("eventData");
            // 5 sends every top-of-book update.
            json.WriteNumber("thresholdLevel", 5);
            json.WriteStartArray("tickers");
            foreach (var ticker in _tickers)
            {
                json.WriteStringValue(ticker);
            }

            json.WriteEndArray();
            json.WriteEndObject();
            json.WriteEndObject();
        }

        return buffer.WrittenMemory;
    }

    /// <summary>Reads one whole message. Returns null when Tiingo closes the connection.</summary>
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
                throw new InvalidDataException("A message from Tiingo is too large.");
            }

            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(buffer.WrittenSpan);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Subscribed to Tiingo prices for {Count} instruments")]
    private static partial void LogSubscribed(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Loaded the latest Tiingo prices for {Count} of {Total} instruments")]
    private static partial void LogLatestPricesLoaded(ILogger logger, int count, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the latest Tiingo prices, continuing with the stream")]
    private static partial void LogLatestPricesFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Tiingo stream ended, reconnecting in {Delay}")]
    private static partial void LogStreamEnded(ILogger logger, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Tiingo stream failed, reconnecting in {Delay}")]
    private static partial void LogStreamFailed(ILogger logger, TimeSpan delay, Exception exception);
}
