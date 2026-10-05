using System.Text;
using System.Text.Json;

using Prop.Api.Identity;

namespace Prop.Api.Tests;

/// <summary>How Didit's decisions and statuses are read, and how its webhooks are known by their signature (ADR 0042).</summary>
public sealed class DiditDecisionTests
{
    [Theory]
    [InlineData("Approved", IdentityStatus.Approved)]
    [InlineData("Declined", IdentityStatus.Declined)]
    [InlineData("In Review", IdentityStatus.InReview)]
    [InlineData("Not Started", IdentityStatus.Pending)]
    [InlineData("In Progress", IdentityStatus.Pending)]
    [InlineData("Awaiting User", IdentityStatus.Pending)]
    [InlineData("Resubmitted", IdentityStatus.Pending)]
    [InlineData("Abandoned", IdentityStatus.Expired)]
    [InlineData("Expired", IdentityStatus.Expired)]
    [InlineData("Kyc Expired", IdentityStatus.Expired)]
    public void DiditsStatusesAreOurs(string status, IdentityStatus expected)
    {
        Assert.Equal(expected, DiditChecker.StatusOf(status));
    }

    [Fact]
    public void AnApprovedDecisionGivesTheDocumentsNameBirthAndCountryAndTheExtraChecks()
    {
        using var json = JsonDocument.Parse(
            """
            {
              "session_id": "s1", "status": "Approved",
              "id_verifications": [{ "status": "Approved", "first_name": "Anna", "last_name": "Andersson", "date_of_birth": "1990-04-01", "issuing_state": "SWE", "issuing_state_name": "Sweden" }],
              "poa_verifications": [{ "status": "Approved" }],
              "aml_screenings": [{ "status": "Declined", "total_hits": 2 }]
            }
            """);

        var decision = DiditChecker.DecisionOf(json.RootElement);

        Assert.Equal(new IdentityDecision(IdentityStatus.Approved, "Anna Andersson", new DateOnly(1990, 4, 1), "Sweden", true, false, null), decision);
    }

    [Fact]
    public void ADeclinedDecisionSaysWhyFromEveryChecksWarningsOnce()
    {
        using var json = JsonDocument.Parse(
            """
            {
              "status": "Declined",
              "id_verifications": [{ "status": "Declined", "full_name": "A B", "warnings": [{ "short_description": "Document expired" }, { "short_description": "Document expired" }] }],
              "liveness_checks": [{ "status": "Declined", "warnings": [{ "short_description": "Face not live." }] }]
            }
            """);

        var decision = DiditChecker.DecisionOf(json.RootElement);

        Assert.Equal((IdentityStatus.Declined, "Document expired. Face not live."), (decision.Status, decision.Reason));
        Assert.Equal("The check did not pass.", DiditChecker.DecisionOf(JsonDocument.Parse("""{ "status": "Declined" }""").RootElement).Reason);
    }

    [Fact]
    public void OnlyTheBodySignedWithOurSecretIsDidits()
    {
        var body = Encoding.UTF8.GetBytes("""{"session_id":"s1","status":"Approved"}""");
        var signature = DiditSignature.Of("secret", body);

        Assert.True(DiditSignature.IsValid("secret", signature, body));
        Assert.True(DiditSignature.IsValid("secret", signature.ToUpperInvariant(), body));
        Assert.False(DiditSignature.IsValid("other", signature, body));
        Assert.False(DiditSignature.IsValid("secret", signature, Encoding.UTF8.GetBytes("""{"session_id":"s2","status":"Approved"}""")));
        Assert.False(DiditSignature.IsValid("secret", null, body));
        Assert.False(DiditSignature.IsValid("", DiditSignature.Of("", body), body));
    }
}
