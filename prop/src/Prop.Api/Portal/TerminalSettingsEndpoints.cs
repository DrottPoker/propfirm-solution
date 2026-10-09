using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Api;
using Prop.Api.Trading;

namespace Prop.Api.Portal;

/// <summary>
/// How orders start in the firm's terminal, in the admin panel (ADR 0058): whether each order asks before it is sent, and
/// the size a ticket starts with. They are kept on the trading platform in the firm's terminal profile, whose other parts
/// we keep up to date, and terminals take them the next time they open. A trader's own choice in the terminal's settings
/// comes before the firm's.
/// </summary>
internal static class TerminalSettingsEndpoints
{
    public const decimal MaxStartingLots = 1_000m;

    public static RouteGroupBuilder MapTerminalSettings(this RouteGroupBuilder admin)
    {
        admin.MapGet("/terminal", GetAsync);
        admin.MapPut("/terminal", SaveAsync);
        return admin;
    }

    private static async Task<Results<Ok<TerminalSettings>, ProblemHttpResult>> GetAsync(
        HttpContext context,
        ITradingPlatform trading,
        CancellationToken cancellationToken)
    {
        if (PortalFirmFilter.FirmOf(context).Trading is not { } server)
        {
            return NotReady();
        }

        try
        {
            return TypedResults.Ok(SettingsOf(await trading.GetTerminalProfileAsync(server, cancellationToken)));
        }
        catch (TradingPlatformUnavailableException)
        {
            return Unavailable();
        }
    }

    private static async Task<Results<Ok<TerminalSettings>, ProblemHttpResult>> SaveAsync(
        TerminalSettings request,
        HttpContext context,
        ITradingPlatform trading,
        CancellationToken cancellationToken)
    {
        if (PortalFirmFilter.FirmOf(context).Trading is not { } server)
        {
            return NotReady();
        }

        if (ProblemOf(request) is { } problem)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, problem);
        }

        try
        {
            var current = await trading.GetTerminalProfileAsync(server, cancellationToken);
            var profile = current with
            {
                ConfirmOrders = request.ConfirmOrders,
                StartingSize = new TerminalStartingSize(request.StartingSize, request.StartingSize == StartingSizeKind.Smallest ? null : request.StartingValue),
            };
            await trading.SetTerminalProfileAsync(server, profile, cancellationToken);
            return TypedResults.Ok(SettingsOf(profile));
        }
        catch (TradingPlatformRejectedException)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, "The trading platform did not accept the settings.");
        }
        catch (TradingPlatformUnavailableException)
        {
            return Unavailable();
        }
    }

    /// <summary>Why the settings cannot be used, or null when they can.</summary>
    public static string? ProblemOf(TerminalSettings settings) => settings switch
    {
        _ when !Enum.IsDefined(settings.StartingSize) => "Choose how a new order's size starts.",
        { StartingSize: StartingSizeKind.Lots, StartingValue: not (> 0m and <= MaxStartingLots) } =>
            "Write the size in lots, above 0 and at most 1,000.",
        { StartingSize: StartingSizeKind.Lots, StartingValue: { } lots } when decimal.Round(lots, 2) != lots => "Write the size in lots with at most 2 decimals.",
        { StartingSize: StartingSizeKind.RiskOfRoom, StartingValue: not (>= 0.01m and <= 100m) } =>
            "Write the share of what is left of today's loss limit, from 0.01 to 100 percent.",
        _ => null,
    };

    private static TerminalSettings SettingsOf(TerminalProfile profile) =>
        new(profile.ConfirmOrders, profile.StartingSize.Kind, profile.StartingSize.Value);

    private static ProblemHttpResult NotReady() =>
        AccountActions.Problem(StatusCodes.Status409Conflict, "Your trading server is still being set up. Try again in a minute.");

    private static ProblemHttpResult Unavailable() =>
        AccountActions.Problem(StatusCodes.Status503ServiceUnavailable, "The trading platform cannot be reached right now. Try again shortly.");
}

/// <summary>
/// How orders start in the firm's terminal: whether each order asks before it is sent, and the size a ticket starts with,
/// the smallest the instrument allows, a number of lots, or what risks a share of what is left of today's loss limit at
/// the order's stop loss. <paramref name="StartingValue"/> is the lots or the percent, and null for the smallest.
/// </summary>
public sealed record TerminalSettings(bool ConfirmOrders, StartingSizeKind StartingSize, decimal? StartingValue);
