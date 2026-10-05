using System.Net;
using System.Text;

namespace Prop.Api.Tests.Support;

/// <summary>Answers like Cloudflare Turnstile's check of an answer: only <see cref="GoodAnswer"/> passes. Can act as a check that is down.</summary>
internal sealed class FakeRobotCheck : HttpMessageHandler
{
    public const string GoodAnswer = "a-good-answer";

    public const string SiteKey = "a-site-key";

    public const string SecretKey = "a-secret-key";

    public bool Down { get; set; }

    /// <summary>Settings that turn the robot check on, with this fake answering.</summary>
    public static Dictionary<string, string> Settings() => new()
    {
        ["RobotCheck:SiteKey"] = SiteKey,
        ["RobotCheck:SecretKey"] = SecretKey,
        ["RobotCheck:VerifyUrl"] = "https://robots.test/siteverify",
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Down)
        {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        }

        var form = System.Web.HttpUtility.ParseQueryString(await request.Content!.ReadAsStringAsync(cancellationToken));
        var passed = form["secret"] == SecretKey && form["response"] == GoodAnswer;
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"success":{{(passed ? "true" : "false")}}}""", Encoding.UTF8, "application/json"),
        };
    }
}
