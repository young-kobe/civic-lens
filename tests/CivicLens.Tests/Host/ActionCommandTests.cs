using CivicLens.Application.Actions;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ActionCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task StatusReportsZeroCountsForEveryOutcomeBeforeAnyCandidateExists()
    {
        var result = await RunAsync("auth0|owner", "actions", "draft", "status");

        Assert.Equal(0, result.ExitCode);
        foreach (var view in new[] { "pending", "drafted", "unchanged", "superseded", "noTrackedOfficial", "failed" })
            Assert.Contains($"\"{view}\":0", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("drafted")]
    [InlineData("unchanged")]
    [InlineData("superseded")]
    [InlineData("no-official")]
    [InlineData("pending")]
    [InlineData("failed")]
    public async Task ListAcceptsEachOutcomeFilter(string outcome)
    {
        var result = await RunAsync("auth0|owner", "actions", "draft", "list", outcome, "5");

        Assert.Equal((0, "[]"), (result.ExitCode, result.Output.Trim()));
    }

    [Theory]
    [InlineData("actions")]
    [InlineData("actions", "draft")]
    [InlineData("actions", "draft", "list", "passed")]
    [InlineData("actions", "draft", "requeue")]
    [InlineData("actions", "draft", "requeue", "--blocked")]
    [InlineData("actions", "draft", "requeue", "--blocked", "--code")]
    [InlineData("actions", "draft", "run", "x")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use actions draft status", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequeueReportsZeroWhenNothingFailedAndRejectsAnIdThatIsNotAHash()
    {
        var all = await RunAsync("auth0|owner", "actions", "draft", "requeue", "--all");
        var bad = await RunAsync("auth0|owner", "actions", "draft", "requeue", "not-a-hash");

        Assert.Equal((0, """{"requeued":0}"""), (all.ExitCode, all.Output.Trim()));
        Assert.Equal(2, bad.ExitCode);
    }

    [Fact]
    public async Task RequeueBlockedReportsTheCountAndRefusesAnEmptyCodeSoItNeverMatchesEveryBlockedRow()
    {
        var known = await RunAsync("auth0|owner", "actions", "draft", "requeue", "--blocked", "--code", ActionDraftErrorCodes.TitleTooLong);
        var empty = await RunAsync("auth0|owner", "actions", "draft", "requeue", "--blocked", "--code", " ");

        Assert.Equal((0, """{"requeued":0}"""), (known.ExitCode, known.Output.Trim()));
        Assert.Equal(2, empty.ExitCode);
        Assert.Contains("errorCode", empty.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("requeue", "--all")]
    [InlineData("requeue", "--blocked", "--code", ActionDraftErrorCodes.TitleTooLong)]
    public async Task TheAiDrafterCannotActAsTheOwner(params string[] command)
    {
        var result = await RunAsync("civic-lens:analysis", ["actions", "draft", .. command]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("The configured actor is not the owner.", result.Error, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string owner, params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_REVIEW_OWNER"] = owner }, arguments);
}
