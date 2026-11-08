using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BillBackfillCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;
    private string artifactDirectory = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        artifactDirectory = Directory.CreateTempSubdirectory("bill-backfill-").FullName;
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(artifactDirectory, true);
    }

    [Theory]
    [InlineData("actions")]
    [InlineData("subjects")]
    [InlineData("status")]
    public async Task WithNoLimitTheBackfillRunsWithTheDefaultLimitSoTheDocumentedCommandWorks(string kind)
    {
        var result = await RunAsync("bills", kind, "backfill");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"examined\":0", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("actions")]
    [InlineData("subjects")]
    [InlineData("status")]
    public async Task AGivenLimitIsAccepted(string kind)
    {
        var result = await RunAsync("bills", kind, "backfill", "5");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"examined\":0", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("actions", "many")]
    [InlineData("actions", "-1")]
    [InlineData("actions", "0")]
    [InlineData("subjects", "many")]
    [InlineData("subjects", "-1")]
    [InlineData("subjects", "0")]
    [InlineData("status", "many")]
    [InlineData("status", "-1")]
    [InlineData("status", "0")]
    public async Task ABadLimitIsRefused(string kind, string limit)
    {
        var result = await RunAsync("bills", kind, "backfill", limit);

        Assert.Equal(2, result.ExitCode);
    }

    [Theory]
    [InlineData("actions")]
    [InlineData("subjects")]
    [InlineData("status")]
    public async Task TooManyArgumentsShowTheUsage(string kind)
    {
        var result = await RunAsync("bills", kind, "backfill", "5", "6");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use bills actions backfill [limit], bills subjects backfill [limit], bills status backfill [limit], bills text backfill [limit]", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("[\"119-hr-1943\",\"119-hr-1943\"]")]
    [InlineData("[\"119-HR-1943\"]")]
    [InlineData("[\"119-hr-01943\"]")]
    [InlineData("[\"hr-1943\"]")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task ABadOrRepeatedBillIdRefusesTheTextBackfillBeforeItReadsTheConfigurationOrAdmitsAnything(string json)
    {
        var billsFile = Path.Combine(artifactDirectory, "bills.json");
        File.WriteAllText(billsFile, json);

        var result = await RunAsync("bills", "text", "backfill", "--bills", billsFile);

        Assert.Equal(2, result.ExitCode);
        Assert.DoesNotContain("CIVIC_LENS_COLLECTION_CONFIG", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("examined", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTextBackfillRefusesAConfigurationWithoutAnEnabledTextSource()
    {
        var result = await RunWithConfigAsync(Path.Combine(RepositoryRoot.FullName, "config", "federal-coverage.json"), "bills", "text", "backfill");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("no enabled bill text source", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheTextBackfillTakesALimitOrTheFileOfTheMeasureBills()
    {
        var config = Path.Combine(artifactDirectory, "collection.json");
        File.WriteAllText(config, File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "federal-coverage.json"))
            .Replace("\"apiParser\":\"congressBillText\",\"checkIntervalSeconds\":86400,\"enabled\":false", "\"apiParser\":\"congressBillText\",\"checkIntervalSeconds\":86400,\"enabled\":true", StringComparison.Ordinal));
        var measureBills = Path.Combine(RepositoryRoot.FullName, "config", "action-text-measure-bills.json");

        var byLimit = await RunWithConfigAsync(config, "bills", "text", "backfill", "5");
        var byDefault = await RunWithConfigAsync(config, "bills", "text", "backfill");
        var byFile = await RunWithConfigAsync(config, "bills", "text", "backfill", "--bills", measureBills);

        Assert.All(new[] { byLimit, byDefault, byFile }, result =>
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("\"examined\":0", result.Output, StringComparison.Ordinal);
        });
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] arguments) =>
        RunWithConfigAsync(null, arguments);

    private Task<(int ExitCode, string Output, string Error)> RunWithConfigAsync(string? collectionConfig, params string[] arguments)
    {
        var environment = new Dictionary<string, string>
        {
            ["CIVIC_LENS_REVIEW_OWNER"] = "auth0|owner",
            ["CIVIC_LENS_ARTIFACT_DIRECTORY"] = artifactDirectory
        };
        if (collectionConfig is not null) environment["CIVIC_LENS_COLLECTION_CONFIG"] = collectionConfig;
        return HostProcess.RunWithEnvironmentAsync(connectionString, null, environment, arguments);
    }
}
