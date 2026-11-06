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

    [Fact]
    public async Task RedraftStaleReportsACountForEachStageOrForTheNamedStage()
    {
        var all = await RunAsync("auth0|owner", "analyses", "redraft", "--stale");
        var votes = await RunAsync("auth0|owner", "analyses", "redraft", "--stale", "votes");
        var documents = await RunAsync("auth0|owner", "analyses", "redraft", "--stale", "document-changes");

        Assert.Equal((0, """{"requeued":{"actions":0,"votes":0,"statements":0,"document-changes":0,"briefs":0}}"""), (all.ExitCode, all.Output.Trim()));
        Assert.Equal((0, """{"requeued":{"votes":0}}"""), (votes.ExitCode, votes.Output.Trim()));
        Assert.Equal((0, """{"requeued":{"document-changes":0}}"""), (documents.ExitCode, documents.Output.Trim()));
    }

    [Fact]
    public async Task RedraftStaleRefusesAnUnknownStageAndTheAiDrafter()
    {
        var unknown = await RunAsync("auth0|owner", "analyses", "redraft", "--stale", "documents");
        var drafter = await RunAsync("civic-lens:analysis", "analyses", "redraft", "--stale");

        Assert.Equal(2, unknown.ExitCode);
        Assert.Contains("Unknown stage", unknown.Error, StringComparison.Ordinal);
        Assert.Equal(2, drafter.ExitCode);
        Assert.Contains("The configured actor is not the owner.", drafter.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("analyses")]
    [InlineData("analyses", "requeue")]
    [InlineData("analyses", "requeue", "a", "b")]
    [InlineData("analyses", "retry", "--all")]
    [InlineData("analyses", "redraft")]
    [InlineData("analyses", "redraft", "votes")]
    [InlineData("analyses", "redraft", "--stale", "votes", "extra")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use analyses requeue", result.Error, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string owner, params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_REVIEW_OWNER"] = owner }, arguments);
}
