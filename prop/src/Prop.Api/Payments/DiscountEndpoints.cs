using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Api;
using Prop.Api.Challenges;
using Prop.Api.Portal;

namespace Prop.Api.Payments;

/// <summary>The firm's discount codes in the admin panel (ADR 0036).</summary>
internal static class DiscountEndpoints
{
    public static RouteGroupBuilder MapAdminDiscounts(this RouteGroupBuilder admin)
    {
        admin.MapGet("/discounts", ListAsync);
        admin.MapPost("/discounts", CreateAsync);
        admin.MapPut("/discounts/{codeId:guid}/active", SetActiveAsync);
        admin.MapDelete("/discounts/{codeId:guid}", DeleteAsync);
        return admin;
    }

    private static async Task<Ok<List<DiscountCodeResponse>>> ListAsync(HttpContext context, DiscountStore discounts, TimeProvider time, CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var codes = await discounts.ListAsync(firm.Id, time.GetUtcNow(), cancellationToken);
        return TypedResults.Ok(codes.Select(DiscountCodeResponse.From).ToList());
    }

    private static async Task<Results<Created<DiscountCodeResponse>, ProblemHttpResult>> CreateAsync(
        DiscountCodeRequest request,
        HttpContext context,
        DiscountStore discounts,
        ChallengeCatalog challenges,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var firm = PortalFirmFilter.FirmOf(context);
        var now = time.GetUtcNow();
        var ids = (await challenges.ListAsync(firm.Id, cancellationToken)).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        if (DiscountRules.Problem(request, ids, now) is { } problem)
        {
            return AccountActions.Problem(StatusCodes.Status422UnprocessableEntity, problem);
        }

        var code = new DiscountCode(
            Guid.CreateVersion7(now),
            firm.Id,
            request.Code!.Trim(),
            request.PercentOff,
            request.AmountOff,
            request.AmountOff is null ? null : request.Currency,
            request.ChallengeIds is { } chosen ? [.. chosen.Distinct(StringComparer.Ordinal)] : null,
            request.MaxUses,
            request.ExpiresAt,
            request.ForRetries,
            true,
            now,
            0);
        return await discounts.CreateAsync(code, cancellationToken)
            ? TypedResults.Created($"/api/portal/admin/discounts/{code.Id}", DiscountCodeResponse.From(code))
            : AccountActions.Problem(StatusCodes.Status409Conflict, "You already have a code with these letters.");
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> SetActiveAsync(
        Guid codeId,
        DiscountActiveRequest request,
        HttpContext context,
        DiscountStore discounts,
        CancellationToken cancellationToken) =>
        await discounts.SetActiveAsync(PortalFirmFilter.FirmOf(context).Id, codeId, request.Active, cancellationToken) ? TypedResults.NoContent() : UnknownCode();

    // A code an order used stays, so the order still says what it paid. The firm turns it off instead.
    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteAsync(Guid codeId, HttpContext context, DiscountStore discounts, CancellationToken cancellationToken) =>
        await discounts.DeleteAsync(PortalFirmFilter.FirmOf(context).Id, codeId, cancellationToken) switch
        {
            true => TypedResults.NoContent(),
            false => AccountActions.Problem(StatusCodes.Status409Conflict, "Orders used the code, so it stays with them. Turn it off instead."),
            null => UnknownCode(),
        };

    private static ProblemHttpResult UnknownCode() => AccountActions.Problem(StatusCodes.Status404NotFound, "No such code.");
}
