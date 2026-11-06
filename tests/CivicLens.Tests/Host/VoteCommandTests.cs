using System.Text.Json;
using CivicLens.Application.Votes;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task StatusReportsZeroCountsForEveryOutcomeAndZeroWaitingVotesBeforeAnyCandidateExists()
    {
        var result = await RunAsync("auth0|owner", "votes", "draft", "status");

        Assert.Equal(0, result.ExitCode);
        using var status = JsonDocument.Parse(result.Output);
        var queue = status.RootElement.GetProperty("queue");
        Assert.Contains("chargedToday", queue.EnumerateObject().Select(property => property.Name));
        foreach (var view in new[] { "pending", "drafted", "unchanged", "superseded", "noTrackedMember", "failed" })
            Assert.Equal(0, queue.GetProperty("counts").GetProperty(view).GetInt32());
        Assert.Equal(0, status.RootElement.GetProperty("waitingForBill").GetInt32());
    }

    [Theory]
    [InlineData("drafted")]
    [InlineData("unchanged")]
    [InlineData("superseded")]
    [InlineData("no-member")]
    [InlineData("pending")]
    [InlineData("failed")]
    public async Task ListAcceptsEachOutcomeFilter(string outcome)
    {
        var result = await RunAsync("auth0|owner", "votes", "draft", "list", outcome, "5");

        Assert.Equal((0, "[]"), (result.ExitCode, result.Output.Trim()));
    }

    [Theory]
    [InlineData("votes")]
    [InlineData("votes", "draft")]
    [InlineData("votes", "draft", "list", "passed")]
    [InlineData("votes", "draft", "requeue")]
    [InlineData("votes", "draft", "requeue", "--blocked")]
    [InlineData("votes", "draft", "requeue", "--blocked", "--code")]
    [InlineData("votes", "draft", "run", "x")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use votes draft status", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequeueReportsZeroWhenNothingFailedAndRejectsAnIdThatIsNotAHash()
    {
        var all = await RunAsync("auth0|owner", "votes", "draft", "requeue", "--all");
        var bad = await RunAsync("auth0|owner", "votes", "draft", "requeue", "not-a-hash");

        Assert.Equal((0, """{"requeued":0}"""), (all.ExitCode, all.Output.Trim()));
        Assert.Equal(2, bad.ExitCode);
    }

    [Fact]
    public async Task RequeueBlockedReportsTheCountAndRefusesAnEmptyCodeSoItNeverMatchesEveryBlockedRow()
    {
        var known = await RunAsync("auth0|owner", "votes", "draft", "requeue", "--blocked", "--code", VoteDraftErrorCodes.FactTooLong);
        var empty = await RunAsync("auth0|owner", "votes", "draft", "requeue", "--blocked", "--code", " ");

        Assert.Equal((0, """{"requeued":0}"""), (known.ExitCode, known.Output.Trim()));
        Assert.Equal(2, empty.ExitCode);
        Assert.Contains("errorCode", empty.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("requeue", "--all")]
    [InlineData("requeue", "--blocked", "--code", VoteDraftErrorCodes.FactTooLong)]
    public async Task TheAiDrafterCannotActAsTheOwner(params string[] command)
    {
        var result = await RunAsync("civic-lens:analysis", ["votes", "draft", .. command]);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("The configured actor is not the owner.", result.Error, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string owner, params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_REVIEW_OWNER"] = owner }, arguments);
}
