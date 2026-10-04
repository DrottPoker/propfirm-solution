using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Api;
using Prop.Api.Firms;
using Prop.Api.Trading;

namespace Prop.Api.Portal;

/// <summary>
/// The firm's trading conditions in the admin panel (ADR 0027): which instruments its traders trade, with what leverage,
/// spread markup and commission. They are kept on the trading platform, in the firm's group, and apply at once to every
/// account of the firm, also to open positions.
/// </summary>
internal static class TradingConditionsEndpoints
{
    public const int MaxLeverage = 1_000;
    public const int MaxSpreadMarkupPoints = 10_000;
    public const decimal MaxCommission = 1_000m;

    public static RouteGroupBuilder MapTradingConditions(this RouteGroupBuilder admin)
    {
        admin.MapGet("/trading-conditions", GetAsync);
        admin.MapPut("/trading-conditions", SaveAsync);
        return admin;
    }

    /// <summary>Every instrument on the platform, with the firm's conditions for those its traders trade.</summary>
    private static async Task<Results<Ok<TradingConditionsResponse>, ProblemHttpResult>> GetAsync(
        HttpContext context,
        ITradingPlatform trading,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (firm.Trading is not { } server)
        {
            return NotReady();
        }

        try
        {
            return TypedResults.Ok(await ConditionsAsync(server, trading, cancellationToken));
        }
        catch (TradingPlatformUnavailableException)
        {
            return Unavailable();
        }
    }

    /// <summary>
    /// The instruments the firm's traders trade, with their conditions. Instruments left out are no longer traded; one with
    /// open positions or orders cannot be left out (409).
    /// </summary>
    private static async Task<Results<Ok<TradingConditionsResponse>, ProblemHttpResult>> SaveAsync(
        TradingConditionsRequest request,
        HttpContext context,
        ITradingPlatform trading,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        if (firm.Trading is not { } server)
        {
            return NotReady();
        }

        var symbols = request.Symbols ?? [];
        if (symbols.Count == 0)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "Choose at least one instrument for your traders.");
        }

        if (symbols.GroupBy(s => s.Symbol).FirstOrDefault(g => g.Count() > 1) is { } twice)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"{twice.Key} is there twice.");
        }

        if (symbols.FirstOrDefault(s => s.Leverage is < 1 or > MaxLeverage) is { } leverage)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"{leverage.Symbol}: the leverage must be 1 to {MaxLeverage}.");
        }

        if (symbols.FirstOrDefault(s => s.SpreadMarkupPoints is < 0 or > MaxSpreadMarkupPoints) is { } markup)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"{markup.Symbol}: the spread markup must be 0 to {MaxSpreadMarkupPoints} points.");
        }

        if (symbols.FirstOrDefault(s => s.CommissionPerLotPerSide is < 0m or > MaxCommission || decimal.Round(s.CommissionPerLotPerSide, 2) != s.CommissionPerLotPerSide) is { } commission)
        {
            return AccountActions.Problem(
                StatusCodes.Status422UnprocessableEntity,
                $"{commission.Symbol}: the commission must be 0 to {MaxCommission:0} per lot and side, in cents.");
        }

        try
        {
            var instruments = await trading.GetInstrumentsAsync(server, cancellationToken);
            if (symbols.FirstOrDefault(s => !instruments.Any(i => i.Symbol == s.Symbol)) is { } unknown)
            {
                return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, $"The platform has no instrument {unknown.Symbol}.");
            }

            await trading.SetGroupSymbolsAsync(
                server,
                [.. symbols.Select(s => new TradingSymbolConditions(s.Symbol, s.Leverage, s.SpreadMarkupPoints, s.CommissionPerLotPerSide))],
                cancellationToken);
            return TypedResults.Ok(await ConditionsAsync(server, trading, cancellationToken));
        }
        catch (TradingPlatformRejectedException exception)
        {
            return exception.Reason switch
            {
                "SymbolInUse" => AccountActions.Problem(
                    StatusCodes.Status409Conflict,
                    "An instrument you turned off has open trades or orders. Keep it until they are closed."),
                "GroupNotChangeable" => AccountActions.Problem(
                    StatusCodes.Status409Conflict,
                    "Your trading conditions are set by us, so they cannot be changed here."),
                _ => AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The trading platform did not accept the conditions."),
            };
        }
        catch (TradingPlatformUnavailableException)
        {
            return Unavailable();
        }
    }

    private static async Task<TradingConditionsResponse> ConditionsAsync(FirmTrading server, ITradingPlatform trading, CancellationToken cancellationToken)
    {
        var instruments = await trading.GetInstrumentsAsync(server, cancellationToken);
        var group = await trading.GetGroupAsync(server, cancellationToken)
            ?? throw new TradingPlatformRejectedException($"The firm's group {server.Group} is not on the trading platform.");
        return new TradingConditionsResponse(
            group.Currency,
            group.Changeable,
            [
                .. instruments.Select(i => group.Symbols.FirstOrDefault(s => s.Symbol == i.Symbol) is { } conditions
                    ? new TradingConditionsSymbol(i.Symbol, i.BaseCurrency, i.QuoteCurrency, i.ContractSize, true, conditions.Leverage, conditions.SpreadMarkupPoints, conditions.CommissionPerLotPerSide)
                    : new TradingConditionsSymbol(i.Symbol, i.BaseCurrency, i.QuoteCurrency, i.ContractSize, false, null, null, null)),
            ]);
    }

    private static ProblemHttpResult NotReady() =>
        AccountActions.Problem(StatusCodes.Status409Conflict, "Your trading server is still being set up. Try again in a minute.");

    private static ProblemHttpResult Unavailable() =>
        AccountActions.Problem(StatusCodes.Status503ServiceUnavailable, "The trading platform cannot be reached right now. Try again shortly.");
}

/// <summary>
/// The firm's trading conditions: the account currency, whether the firm can change them (not for a firm we set up in our
/// configuration), and every instrument on the platform.
/// </summary>
public sealed record TradingConditionsResponse(string Currency, bool Changeable, IReadOnlyList<TradingConditionsSymbol> Symbols);

/// <summary>
/// An instrument and whether the firm's traders trade it. Margin is the position's value divided by <paramref name="Leverage"/>;
/// <paramref name="SpreadMarkupPoints"/> are added to the price feed's spread; <paramref name="CommissionPerLotPerSide"/> is charged
/// in the account currency on open and on close. The conditions are empty for an instrument that is not traded.
/// </summary>
public sealed record TradingConditionsSymbol(
    string Symbol,
    string BaseCurrency,
    string QuoteCurrency,
    decimal ContractSize,
    bool Enabled,
    int? Leverage,
    int? SpreadMarkupPoints,
    decimal? CommissionPerLotPerSide);

/// <summary>The instruments the firm's traders should trade, each with its conditions.</summary>
public sealed record TradingConditionsRequest(IReadOnlyList<TradingSymbolRequest>? Symbols);

public sealed record TradingSymbolRequest(string Symbol, int Leverage, int SpreadMarkupPoints, decimal CommissionPerLotPerSide);
