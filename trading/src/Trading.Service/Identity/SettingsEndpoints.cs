using System.Text.Json;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.Http.HttpResults;

using Trading.Service.Api;

namespace Trading.Service.Identity;

/// <summary>
/// The trader's choices in the terminal, such as favorites, volumes, indicators and drawings, kept on their login so
/// they follow them to another device (ADR 0052). The terminal decides what each key holds.
/// </summary>
internal static partial class SettingsEndpoints
{
    /// <summary>More keys than the terminal uses, with room for a drawing list per symbol.</summary>
    public const int MaxSettings = 200;

    /// <summary>Enough for the longest list the terminal keeps, a hundred drawings.</summary>
    public const int MaxValueLength = 64 * 1024;

    public static IEndpointRouteBuilder MapSettingsApi(this IEndpointRouteBuilder app)
    {
        var settings = app.MapGroup("/api/me/settings").WithTags("Settings").RequireAuthorization().RequireRateLimiting(RateLimits.Trader);
        settings.MapGet("", GetSettingsAsync);
        settings.MapPut("/{key}", SetSettingAsync);
        settings.MapDelete("/{key}", DeleteSettingAsync);
        return app;
    }

    /// <summary>Every setting the trader has stored, by key.</summary>
    private static async Task<Results<Ok<SettingsResponse>, UnauthorizedHttpResult>> GetSettingsAsync(HttpContext context, IUserStore users, CancellationToken cancellationToken)
    {
        if (CurrentUser.IdOf(context.User) is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        var stored = await users.SettingsOfAsync(userId, cancellationToken);
        return TypedResults.Ok(new SettingsResponse(stored.ToDictionary(s => s.Key, s => JsonDocument.Parse(s.Value).RootElement.Clone(), StringComparer.Ordinal)));
    }

    /// <summary>
    /// Stores any JSON value under the key, replacing the one before. Keys are letters, digits, dots, dashes and
    /// underscores, at most 120 long. 413 for a value over 64 KB, and 422 when the trader already has 200 settings.
    /// </summary>
    private static async Task<Results<NoContent, UnauthorizedHttpResult, ProblemHttpResult>> SetSettingAsync(
        string key,
        JsonElement value,
        HttpContext context,
        IUserStore users,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.IdOf(context.User) is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        if (!KeyPattern().IsMatch(key))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The key may have letters, digits, dots, dashes and underscores, at most 120.");
        }

        var json = value.GetRawText();
        if (json.Length > MaxValueLength)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: $"A setting may be at most {MaxValueLength / 1024} KB.");
        }

        return await users.SetSettingAsync(userId, key, json, MaxSettings, time.GetUtcNow(), cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: $"At most {MaxSettings} settings can be stored.");
    }

    private static async Task<Results<NoContent, UnauthorizedHttpResult>> DeleteSettingAsync(
        string key,
        HttpContext context,
        IUserStore users,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (CurrentUser.IdOf(context.User) is not { } userId)
        {
            return TypedResults.Unauthorized();
        }

        await users.SetSettingAsync(userId, key, null, MaxSettings, time.GetUtcNow(), cancellationToken);
        return TypedResults.NoContent();
    }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,120}$")]
    private static partial Regex KeyPattern();
}

/// <summary>The trader's settings in the terminal, by key.</summary>
public sealed record SettingsResponse(IReadOnlyDictionary<string, JsonElement> Settings);
