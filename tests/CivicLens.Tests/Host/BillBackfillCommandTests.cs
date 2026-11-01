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
    [InlineData("subjects")]
    [InlineData("status")]
    public async Task WithNoLimitTheBackfillRunsWithTheDefaultLimitSoTheDocumentedCommandWorks(string kind)
    {
        var result = await RunAsync("bills", kind, "backfill");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"examined\":0", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("subjects")]
    [InlineData("status")]
    public async Task AGivenLimitIsAccepted(string kind)
    {
        var result = await RunAsync("bills", kind, "backfill", "5");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("\"examined\":0", result.Output, StringComparison.Ordinal);
    }

    [Theory]
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
    [InlineData("subjects")]
    [InlineData("status")]
    public async Task TooManyArgumentsShowTheUsage(string kind)
    {
        var result = await RunAsync("bills", kind, "backfill", "5", "6");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use bills subjects backfill [limit] or bills status backfill [limit].", result.Error, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string>
            {
                ["CIVIC_LENS_REVIEW_OWNER"] = "auth0|owner",
                ["CIVIC_LENS_ARTIFACT_DIRECTORY"] = artifactDirectory
            }, arguments);
}
