using System.Text.Json;
using CivicLens.Application.Configuration;
using CivicLens.Collection.Contracts;
using CivicLens.Host.Review;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Host;

public sealed class DocumentChangeAnalysisConfigurationTests
{
    private const string Database = "Host=127.0.0.1;Port=1;Database=fixture;Username=fixture;Password=secret-sentinel;Timeout=1";

    [Theory]
    [InlineData("CIVIC_LENS_ANALYSIS_DAILY_TOKENS", "100000")]
    [InlineData("CIVIC_LENS_ANALYSIS_DAILY_TOKENS", "not-a-number")]
    public async Task TheWorkerRefusesAPartialOrInvalidDraftingConfiguration(string name, string value)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-drafting-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(config, """
                {"version":2,"people":[{"id":"person","name":"Fixture"}],"sources":[
                  {"id":"source","coverage":[{"personId":"person"}],"url":"https://example.test/page",
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

    [Fact]
    public async Task AKeyAloneLeavesEveryAiStageOffBecauseTheStagesShareIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-key-alone-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(config, """
                {"version":2,"people":[{"id":"person","name":"Fixture"}],"sources":[
                  {"id":"source","coverage":[{"personId":"person"}],"url":"https://example.test/page",
                   "allowedOrigin":"https://example.test","allowedPathPrefix":"/page"}]}
                """);
            var environment = new Dictionary<string, string>
            {
                ["CIVIC_LENS_COLLECTION_CONFIG"] = config,
                ["ANTHROPIC_API_KEY"] = "key-sentinel"
            };

            var result = await HostProcess.RunWithEnvironmentAsync(Database, null, environment,
                "worker", Path.Combine(directory, "collector.dll"), Path.Combine(directory, "captures"), "--once");

            Assert.Equal(1, result.ExitCode);
            Assert.DoesNotContain("key-sentinel", result.Output + result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(true, false, 2)]
    [InlineData(false, false, 1)]
    [InlineData(true, true, 1)]
    public async Task TheWorkerRefusesAnEnabledBillListSourceWithoutAnIssuesConfiguration(bool enabled, bool withIssues, int exitCode)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-bill-issues-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var configuration = CongressBillSourceConfigurationTests.Configuration();
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(config, JsonSerializer.Serialize(
                configuration with { Sources = [configuration.Sources.Single() with { Enabled = enabled }] },
                ConfigurationJson.Options));
            var environment = new Dictionary<string, string> { ["CIVIC_LENS_COLLECTION_CONFIG"] = config };
            if (withIssues)
            {
                var issues = Path.Combine(directory, "issues.json");
                await File.WriteAllTextAsync(issues, JsonSerializer.Serialize(BillGateFixtures.Issues(), CollectionProtocol.JsonOptions));
                environment["CIVIC_LENS_ISSUES_CONFIG"] = issues;
            }

            var result = await HostProcess.RunWithEnvironmentAsync(Database, null, environment,
                "worker", Path.Combine(directory, "collector.dll"), Path.Combine(directory, "captures"), "--once");

            Assert.Equal(exitCode, result.ExitCode);
            Assert.DoesNotContain("secret-sentinel", result.Output + result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("CIVIC_LENS_STATEMENT_GATE_DAILY_TOKENS", "100000", null)]
    [InlineData("CIVIC_LENS_STATEMENT_GATE_DAILY_TOKENS", "not-a-number", "key-sentinel")]
    public async Task TheWorkerRefusesAGateLimitWithoutAKeyOrWithAnInvalidLimit(string name, string value, string? key)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-gate-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(config, """
                {"version":2,"people":[{"id":"person","name":"Fixture"}],"sources":[
                  {"id":"source","coverage":[{"personId":"person"}],"url":"https://example.test/page",
                   "allowedOrigin":"https://example.test","allowedPathPrefix":"/page"}]}
                """);
            var environment = new Dictionary<string, string> { ["CIVIC_LENS_COLLECTION_CONFIG"] = config, [name] = value };
            if (key is not null) environment["ANTHROPIC_API_KEY"] = key;

            var result = await HostProcess.RunWithEnvironmentAsync(Database, null, environment,
                "worker", Path.Combine(directory, "collector.dll"), Path.Combine(directory, "captures"), "--once");

            Assert.Equal(2, result.ExitCode);
            Assert.DoesNotContain("key-sentinel", result.Output + result.Error);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("CIVIC_LENS_STATEMENT_DRAFTING_DAILY_TOKENS", "100000", null)]
    [InlineData("CIVIC_LENS_STATEMENT_DRAFTING_DAILY_TOKENS", "not-a-number", "key-sentinel")]
    [InlineData("CIVIC_LENS_STATEMENT_DRAFTING_RUN_TOKENS", "not-a-number", "key-sentinel")]
    public async Task TheWorkerRefusesAStatementDraftingLimitWithoutAKeyOrWithAnInvalidLimit(string name, string value, string? key)
    {
        var directory = Path.Combine(Path.GetTempPath(), "civic-statement-drafting-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var config = Path.Combine(directory, "config.json");
            await File.WriteAllTextAsync(config, """
                {"version":2,"people":[{"id":"person","name":"Fixture"}],"sources":[
                  {"id":"source","coverage":[{"personId":"person"}],"url":"https://example.test/page",
                   "allowedOrigin":"https://example.test","allowedPathPrefix":"/page"}]}
                """);
            var environment = new Dictionary<string, string> { ["CIVIC_LENS_COLLECTION_CONFIG"] = config, [name] = value };
            if (name.EndsWith("RUN_TOKENS", StringComparison.Ordinal)) environment["CIVIC_LENS_STATEMENT_DRAFTING_DAILY_TOKENS"] = "100000";
            if (key is not null) environment["ANTHROPIC_API_KEY"] = key;

            var result = await HostProcess.RunWithEnvironmentAsync(Database, null, environment,
                "worker", Path.Combine(directory, "collector.dll"), Path.Combine(directory, "captures"), "--once");

            Assert.Equal(2, result.ExitCode);
            Assert.DoesNotContain("key-sentinel", result.Output + result.Error);
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
