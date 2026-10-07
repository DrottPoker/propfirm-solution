using System.Security.Claims;

using Microsoft.AspNetCore.Http.HttpResults;

using Prop.Api.Api;
using Prop.Api.Ops;
using Prop.Api.Portal;

namespace Prop.Api.Incidents;

/// <summary>Incidents (ADR 0053): our staff write and publish them, a firm's administrators act on them, and anyone reads the firm's status page.</summary>
internal static class IncidentEndpoints
{
    public static RouteGroupBuilder MapOpsIncidents(this RouteGroupBuilder staff)
    {
        staff.MapGet("/incidents", (IncidentService incidents, CancellationToken cancellationToken) => ListForStaffAsync(incidents, cancellationToken));
        staff.MapPost(
            "/incidents",
            async (IncidentRequest request, ClaimsPrincipal principal, IncidentService incidents, CancellationToken cancellationToken) =>
                Answer(await incidents.CreateAsync(request, StaffAuth.EmailOf(principal), cancellationToken)));
        staff.MapPut(
            "/incidents/{incidentId:guid}",
            async (Guid incidentId, IncidentRequest request, ClaimsPrincipal principal, IncidentService incidents, CancellationToken cancellationToken) =>
                Answer(await incidents.EditAsync(incidentId, request, StaffAuth.EmailOf(principal), cancellationToken)));
        staff.MapPost(
            "/incidents/{incidentId:guid}/publish",
            async (Guid incidentId, ClaimsPrincipal principal, IncidentService incidents, CancellationToken cancellationToken) =>
                Answer(await incidents.PublishAsync(incidentId, StaffAuth.EmailOf(principal), cancellationToken)));
        staff.MapPost(
            "/incidents/{incidentId:guid}/updates",
            async (Guid incidentId, IncidentUpdateRequest request, ClaimsPrincipal principal, IncidentService incidents, CancellationToken cancellationToken) =>
                Answer(await incidents.PostUpdateAsync(incidentId, request, StaffAuth.EmailOf(principal), cancellationToken)));
        staff.MapPost(
            "/incidents/{incidentId:guid}/dismiss",
            async (Guid incidentId, IncidentService incidents, CancellationToken cancellationToken) => Done(await incidents.DismissAsync(incidentId, cancellationToken)));
        return staff;
    }

    public static RouteGroupBuilder MapAdminIncidents(this RouteGroupBuilder admin)
    {
        admin.MapGet("/incidents", ListForFirmAsync);
        admin.MapGet("/incidents/{incidentId:guid}", GetForFirmAsync);
        admin.MapPut(
            "/incidents/{incidentId:guid}/note",
            async (Guid incidentId, IncidentNoteRequest request, HttpContext context, ClaimsPrincipal principal, IncidentService incidents, CancellationToken cancellationToken) =>
                Done(await incidents.SetNoteAsync(PortalFirmFilter.FirmOf(context), incidentId, request.Text, IncidentService.ActorOf(principal), cancellationToken)));
        admin.MapPost(
            "/incidents/{incidentId:guid}/reinstate",
            async (Guid incidentId, ReinstateRequest request, HttpContext context, ClaimsPrincipal principal, IncidentService incidents, CancellationToken cancellationToken) =>
                Done(await incidents.ReinstateAsync(PortalFirmFilter.FirmOf(context), incidentId, request, IncidentService.ActorOf(principal), cancellationToken)));
        admin.MapPost(
            "/incidents/{incidentId:guid}/credit",
            async (Guid incidentId, CreditRequest request, HttpContext context, ClaimsPrincipal principal, IncidentService incidents, CancellationToken cancellationToken) =>
                Done(await incidents.CreditAsync(PortalFirmFilter.FirmOf(context), incidentId, request, IncidentService.ActorOf(principal), cancellationToken)));
        return admin;
    }

    /// <summary>The firm's status page, for anyone: whether everything runs, and the incidents of the last 90 days.</summary>
    public static RouteGroupBuilder MapStatusPage(this RouteGroupBuilder portal)
    {
        portal.MapGet(
            "/status",
            async (HttpContext context, IncidentService incidents, CancellationToken cancellationToken) =>
                TypedResults.Ok(await incidents.StatusAsync(PortalFirmFilter.FirmOf(context), cancellationToken)));
        return portal;
    }

    /// <summary>Our incidents of the last 90 days and those that go on, with the price feed now and the firms an outage would reach.</summary>
    private static async Task<Ok<OpsIncidentsResponse>> ListForStaffAsync(IncidentService incidents, CancellationToken cancellationToken) =>
        TypedResults.Ok(await incidents.ListForStaffAsync(cancellationToken));

    /// <summary>The published incidents of the last 90 days that concern the firm, and how many go on, for the menu.</summary>
    private static async Task<Ok<FirmIncidentsResponse>> ListForFirmAsync(HttpContext context, IncidentService incidents, CancellationToken cancellationToken) =>
        TypedResults.Ok(await incidents.ListForFirmAsync(PortalFirmFilter.FirmOf(context), cancellationToken));

    /// <summary>The incident, with the firm's accounts it reached and what the firm did for them.</summary>
    private static async Task<Results<Ok<FirmIncidentResponse>, ProblemHttpResult>> GetForFirmAsync(
        Guid incidentId,
        HttpContext context,
        IncidentService incidents,
        CancellationToken cancellationToken) =>
        await incidents.DetailForFirmAsync(PortalFirmFilter.FirmOf(context), incidentId, cancellationToken) is { } incident
            ? TypedResults.Ok(incident)
            : AccountActions.Problem(StatusCodes.Status404NotFound, "There is no such incident.");

    private static Results<Ok<OpsIncidentResponse>, ProblemHttpResult> Answer((IncidentResult Result, OpsIncidentResponse? Incident) outcome) =>
        outcome.Incident is { } incident ? TypedResults.Ok(incident) : AccountActions.Problem(outcome.Result.Status, outcome.Result.Problem!);

    private static Results<NoContent, ProblemHttpResult> Done(IncidentResult result) =>
        result.Succeeded ? TypedResults.NoContent() : AccountActions.Problem(result.Status, result.Problem!);
}
