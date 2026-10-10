using CivicLens.Host.Review;

namespace CivicLens.Tests.Host;

public sealed class DocumentChangeAnalysisConfigurationTests
{
    private const string Database = "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=secret-sentinel;Timeout=1";

    [Theory]
    [InlineData("CIVIC_LENS_ANALYSIS_DAILY_TOKENS", "100000")]
    [InlineData("ANTHROPIC_API_KEY", "key-sentinel")]
    [InlineData("CIVIC_LENS_ANALYSIS_DAILY_TOKENS", "not-a-number")]
    public async Task TheWorkerRefusesAPartialOrInvalidDraftingConfiguration(string name, string value)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-drafting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(config, """
                {"version":1,"people":[{"id":"person","name":"Fixture"}],"sources":[
                  {"id":"source","personIds":["person"],"url":"https://example.test/page",
                   "allowedOrigin":"https://example.test","allowedPathPrefix":"/page"}]}
                """);
            var environment = new Dictionary<string, string> { ["CIVIC_LENS_COLLECTION_CONFIG"] = config, [name] = value };
            if (value == "not-a-number") environment["ANTHROPIC_API_KEY"] = "key-sentinel";

            var result = await HostProcess.RunWithEnvironmentAsync(Database, null, environment,
                "worker", Path.Combine(directory, "collector.dll"), Path.Combine(directory, "captures"), "--once");

            Assert.Equal(2, result.ExitCode);
            Assert.DoesNotContain("key-sentinel", result.Output + result.Error);
            Assert.DoesNotContain("secret-sentinel", result.Output + result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("civic-lens:analysis", "")]
    [InlineData("auth0|owner", "auth0|friend,civic-lens:analysis")]
    public void TheAiDrafterSubjectCannotBeAnOwnerOrReviewer(string owner, string reviewers)
    {
        var configured = reviewers.Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

        var error = Assert.Throws<ArgumentException>(() => ReviewWorkspaceSettings.ValidateSubjects(owner, configured));

        Assert.Contains("AI drafter", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PeopleCanBeOwnersAndReviewers() =>
        ReviewWorkspaceSettings.ValidateSubjects("auth0|owner", new HashSet<string>(["auth0|friend"], StringComparer.Ordinal));
}
