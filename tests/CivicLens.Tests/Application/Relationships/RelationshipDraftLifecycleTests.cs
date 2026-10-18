using CivicLens.Application.Analysis;
using CivicLens.Application.Relationships;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Relationships;

public sealed class RelationshipDraftLifecycleTests
{
    private static readonly string Id = new('f', 64);

    [Theory]
    [InlineData(State.Succeeded)]
    [InlineData(State.Blocked)]
    [InlineData(State.Failed)]
    public void AFinishedPairMayReturnToPendingSoANewApprovedRevisionCanRedraftIt(State finished)
    {
        var record = new AnalysisRecord(Id, RelationshipDraftTask.Task, finished, null, 1, 1, null, null);

        Assert.True(RelationshipDraftLifecycle.CanTransition(record,
            new AnalysisCheckpoint(Id, RelationshipDraftTask.Task, finished, null, 1, State.Pending)));
    }

    [Theory]
    [InlineData(RelationshipDraftErrorCodes.Unchanged, true)]
    [InlineData(RelationshipDraftErrorCodes.NoRelationship, true)]
    [InlineData(RelationshipDraftErrorCodes.NoLongerMatches, true)]
    [InlineData(RelationshipDraftErrorCodes.OverByteCap, false)]
    public void ARunningPairMaySettleSucceededWithoutARevisionOnlyForTheThreeSettledReasons(string reason, bool allowed)
    {
        var running = new AnalysisRecord(Id, RelationshipDraftTask.Task, State.Running, "lease", 1, 0, null, null);

        Assert.Equal(allowed, RelationshipDraftLifecycle.CanTransition(running,
            new AnalysisCheckpoint(Id, RelationshipDraftTask.Task, State.Running, "lease", 1, State.Succeeded, reason)));
    }

    [Fact]
    public void ARevisionSettlesOnlyForItsOwnPairWithNoErrorCode()
    {
        var running = new AnalysisRecord(Id, RelationshipDraftTask.Task, State.Running, "lease", 1, 0, null, null);
        var claim = new AnalysisClaim(running, "lease", 1);
        var revision = RelationshipDraftResolver.Resolve(RelationshipFixtures.Output(), RelationshipFixtures.Context(RelationshipFixtures.ActionPair()),
            RelationshipFixtures.Created).Accepted!;
        var own = RelationshipDraftCheckpoint.From(claim, revision with { RecordId = Id });
        var other = RelationshipDraftCheckpoint.From(claim, revision);

        Assert.True(RelationshipDraftLifecycle.CanTransition(running, own));
        Assert.False(RelationshipDraftLifecycle.CanTransition(running, other));
        Assert.False(RelationshipDraftLifecycle.CanTransition(running, own with { ErrorCode = RelationshipDraftErrorCodes.Unchanged }));
    }
}
