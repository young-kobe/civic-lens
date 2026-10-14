using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class AnalysisCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheOwnerRequeuesAllOrOneFailedEntry()
    {
        var all = await RunAsync("auth0|owner", "analyses", "requeue", "--all");
        var one = await RunAsync("auth0|owner", "analyses", "requeue", new string('c', 64));

        Assert.Equal((0, """{"requeued":0}"""), (all.ExitCode, all.Output.Trim()));
        Assert.Equal((0, """{"requeued":0}"""), (one.ExitCode, one.Output.Trim()));
    }

    [Theory]
    [InlineData("analyses")]
    [InlineData("analyses", "requeue")]
    [InlineData("analyses", "requeue", "a", "b")]
    [InlineData("analyses", "retry", "--all")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use analyses requeue", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnInvalidComparisonIdIsRejected()
    {
        var result = await RunAsync("auth0|owner", "analyses", "requeue", "not-a-hash");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("SHA-256", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAiDrafterCannotActAsTheOwner()
    {
        var result = await RunAsync("civic-lens:analysis", "analyses", "requeue", "--all");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("not the owner", result.Error, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string owner, params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_REVIEW_OWNER"] = owner }, arguments);
}
