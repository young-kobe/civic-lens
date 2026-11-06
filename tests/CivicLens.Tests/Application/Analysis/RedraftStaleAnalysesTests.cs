using CivicLens.Application.Analysis;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Analysis;

public sealed class RedraftStaleAnalysesTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);
    private readonly CountingStage actions = new(3);
    private readonly CountingStage votes = new(5);

    [Theory]
    [InlineData("auth0|friend", ReviewRole.Reviewer)]
    [InlineData("civic-lens:analysis", ReviewRole.Owner)]
    public async Task OnlyAHumanOwnerRedrafts(string subject, ReviewRole role)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Redraft().ExecuteAsync(new ReviewActor(subject, role), null));

        Assert.Equal(0, actions.Calls + votes.Calls);
    }

    [Fact]
    public async Task WithoutAStageFilterEveryStageRunsOnceAndEachCountIsReported()
    {
        var requeued = await Redraft().ExecuteAsync(Owner, null);

        Assert.Equal(new Dictionary<string, int> { ["actions"] = 3, ["votes"] = 5 }, requeued);
        Assert.Equal((1, 1), (actions.Calls, votes.Calls));
    }

    [Fact]
    public async Task AStageFilterRunsOnlyThatStage()
    {
        var requeued = await Redraft().ExecuteAsync(Owner, "votes");

        Assert.Equal(new Dictionary<string, int> { ["votes"] = 5 }, requeued);
        Assert.Equal((0, 1), (actions.Calls, votes.Calls));
    }

    [Fact]
    public async Task AnUnknownStageNamesTheKnownStagesAndRunsNothing()
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Redraft().ExecuteAsync(Owner, "statements"));

        Assert.Contains("actions, votes", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, actions.Calls + votes.Calls);
    }

    [Theory]
    [InlineData("task-v1", "prompt-v2", "schema-v1", true)]
    [InlineData("task-v0", "prompt-v2", "schema-v1", false)]
    [InlineData("task-v1", "prompt-v1", "schema-v1", false)]
    [InlineData("task-v1", "prompt-v2", "schema-v0", false)]
    public void ARunIsCurrentOnlyWhenTaskPromptAndSchemaAllMatch(string task, string prompt, string schema, bool current)
    {
        var versions = new AnalysisCurrentVersions("task-v1", ["prompt-v2"], ["schema-v1"]);

        Assert.Equal(current, versions.IsCurrent(task, prompt, schema));
    }

    private RedraftStaleAnalyses Redraft() => new([new("actions", actions), new("votes", votes)]);

    private sealed class CountingStage(int count) : IStaleAnalysisRequeue
    {
        public int Calls { get; private set; }

        public Task<int> RequeueStaleAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(count);
        }
    }
}
