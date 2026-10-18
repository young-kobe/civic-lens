using System.Text.Json;
using CivicLens.Tests.Infrastructure.Collection;

namespace CivicLens.Tests.Host;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RelationshipCommandTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task StatusReportsZeroCountsForEveryOutcomeAndZeroStatementsWithoutACongressBeforeAnythingIsApproved()
    {
        var result = await RunAsync("auth0|owner", "relationships", "draft", "status");

        Assert.Equal(0, result.ExitCode);
        using var status = JsonDocument.Parse(result.Output);
        var counts = status.RootElement.GetProperty("queue").GetProperty("counts");
        foreach (var view in new[] { "pending", "drafted", "unchanged", "noRelationship", "noLongerMatches", "failed" })
            Assert.Equal(0, counts.GetProperty(view).GetInt32());
        Assert.Equal(0, status.RootElement.GetProperty("statementsWithoutCongress").GetInt32());
    }

    [Fact]
    public async Task RefreshPrintsTheNumberOfPairsFoundAndIsOwnerOnly()
    {
        var owner = await RunAsync("auth0|owner", "relationships", "draft", "refresh");
        var ai = await RunAsync("civic-lens:analysis", "relationships", "draft", "refresh");

        Assert.Equal((0, """{"pairs":0}"""), (owner.ExitCode, owner.Output.Trim()));
        Assert.Equal(2, ai.ExitCode);
    }

    [Theory]
    [InlineData("drafted")]
    [InlineData("unchanged")]
    [InlineData("none")]
    [InlineData("unmatched")]
    [InlineData("pending")]
    [InlineData("failed")]
    public async Task ListAcceptsEachOutcomeFilter(string outcome)
    {
        var result = await RunAsync("auth0|owner", "relationships", "draft", "list", outcome, "5");

        Assert.Equal((0, "[]"), (result.ExitCode, result.Output.Trim()));
    }

    [Theory]
    [InlineData("relationships")]
    [InlineData("relationships", "draft")]
    [InlineData("relationships", "draft", "list", "passed")]
    [InlineData("relationships", "draft", "requeue")]
    public async Task BadArgumentsShowTheUsage(params string[] arguments)
    {
        var result = await RunAsync("auth0|owner", arguments);

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Use relationships draft status", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAiDrafterCannotActAsTheOwner()
    {
        var result = await RunAsync("civic-lens:analysis", "relationships", "draft", "requeue", "--all");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("The configured actor is not the owner.", result.Error, StringComparison.Ordinal);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(string owner, params string[] arguments) =>
        HostProcess.RunWithEnvironmentAsync(connectionString, null,
            new Dictionary<string, string> { ["CIVIC_LENS_REVIEW_OWNER"] = owner }, arguments);
}
