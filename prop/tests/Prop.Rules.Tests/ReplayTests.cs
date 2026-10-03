using System.Runtime.CompilerServices;

using Prop.Rules.Tests.Support;

namespace Prop.Rules.Tests;

public sealed class ReplayTests
{
    private const string GoldenFile = "two-step-challenge.jsonl";

    [Fact]
    public void ReplayIsDeterministic()
    {
        Assert.Equal(ReplayScenario.Run(), ReplayScenario.Run());
    }

    // Any change in the rule engine's decisions must be deliberate. Regenerate with UPDATE_GOLDEN=1 and review the diff.
    [Fact]
    public void ReplayMatchesGoldenFile()
    {
        var actual = ReplayScenario.Run();

        if (Environment.GetEnvironmentVariable("UPDATE_GOLDEN") == "1")
        {
            File.WriteAllText(SourceGoldenPath(), string.Join('\n', actual) + "\n");
            Assert.Skip($"Updated {GoldenFile}. Review the diff before committing.");
        }

        var expected = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Golden", GoldenFile));
        Assert.Equal(expected, actual);
    }

    private static string SourceGoldenPath([CallerFilePath] string callerPath = "") =>
        Path.Combine(Path.GetDirectoryName(callerPath)!, "Golden", GoldenFile);
}
