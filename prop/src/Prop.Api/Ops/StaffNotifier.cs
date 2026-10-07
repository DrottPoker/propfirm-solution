using Microsoft.Extensions.Options;

using Prop.Api.Configuration;
using Prop.Api.Email;
using Prop.Api.Firms;

namespace Prop.Api.Ops;

/// <summary>Tells our staff when a firm waits for review. Best effort: our admin view lists the same firms.</summary>
internal sealed partial class StaffNotifier(StaffUsers staff, IEmailSender email, IOptions<PlatformOptions> platform, ILogger<StaffNotifier> logger)
{
    /// <summary>Where our staff review the firm in our admin view.</summary>
    public static Uri ReviewUrl(PlatformOptions platform, string firmId) =>
        new(platform.OpsUrl ?? throw new InvalidOperationException("Platform:OpsUrl is missing."), $"ops/firms/{Uri.EscapeDataString(firmId)}");

    public async Task ApplicationSubmittedAsync(Firm firm, CancellationToken cancellationToken)
    {
        var reviewUrl = ReviewUrl(platform.Value, firm.Id);
        foreach (var member in await staff.ListAsync(cancellationToken))
        {
            try
            {
                await email.SendAsync(PlatformEmails.ApplicationSubmitted(platform.Value.Name, firm.Name, firm.Id, member.Email, reviewUrl), cancellationToken);
            }
            catch (EmailNotSentException exception)
            {
                LogEmailNotSent(logger, exception, firm.Id);
            }
        }
    }

    /// <summary>The price feed stopped, and a draft incident waits for our staff (ADR 0053).</summary>
    public async Task PriceFeedStoppedAsync(Incidents.Incident incident, CancellationToken cancellationToken)
    {
        var incidentUrl = new Uri(platform.Value.OpsUrl ?? throw new InvalidOperationException("Platform:OpsUrl is missing."), $"ops/incidents/{incident.Id}");
        foreach (var member in await staff.ListAsync(cancellationToken))
        {
            try
            {
                await email.SendAsync(PlatformEmails.PriceFeedStopped(platform.Value.Name, member.Email, incident.StartedAt, incidentUrl), cancellationToken);
            }
            catch (EmailNotSentException exception)
            {
                LogIncidentEmailNotSent(logger, exception, incident.Id);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "An email to our staff about the application of firm {FirmId} could not be sent")]
    private static partial void LogEmailNotSent(ILogger logger, Exception exception, string firmId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "An email to our staff about incident {IncidentId} could not be sent")]
    private static partial void LogIncidentEmailNotSent(ILogger logger, Exception exception, Guid incidentId);
}
