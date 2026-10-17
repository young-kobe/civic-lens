using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Votes;

public sealed class VoteDraftLifecycleTests
{
    private const string CandidateId = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    [Fact]
    public void ABlockedVoteDraftMovesBackToPendingBecauseTheOwnerCommandRequeuesItAfterFixingTheCause()
    {
        var record = new AnalysisRecord(CandidateId, VoteDraftTask.Task, State.Blocked, null, 1, 1, null, VoteDraftErrorCodes.FactTooLong);
        var requeue = new AnalysisCheckpoint(CandidateId, VoteDraftTask.Task, State.Blocked, null, 1, State.Pending);

        Assert.True(VoteDraftLifecycle.CanTransition(record, requeue));
    }

    [Fact]
    public void ASucceededVoteDraftStaysFinalBecauseNoCodeRequeuesItAndANewVersionHasItsOwnCandidate()
    {
        var record = new AnalysisRecord(CandidateId, VoteDraftTask.Task, State.Succeeded, null, 1, 1, null, null);
        var requeue = new AnalysisCheckpoint(CandidateId, VoteDraftTask.Task, State.Succeeded, null, 1, State.Pending);

        Assert.False(VoteDraftLifecycle.CanTransition(record, requeue));
    }

    [Theory]
    [InlineData(VoteDraftErrorCodes.Unchanged)]
    [InlineData(VoteDraftErrorCodes.NoTrackedMember)]
    [InlineData(VoteDraftErrorCodes.SupersededByNewerCandidate)]
    public void ASucceededDraftWithoutARevisionNeedsOneOfTheCodesThatExplainWhyNothingWasWritten(string code)
    {
        var record = new AnalysisRecord(CandidateId, VoteDraftTask.Task, State.Running, "lease", 1, 1, null, null);

        Assert.True(VoteDraftLifecycle.CanTransition(record, new AnalysisCheckpoint(CandidateId, VoteDraftTask.Task, State.Running, "lease", 1,
            State.Succeeded, code)));
        Assert.False(VoteDraftLifecycle.CanTransition(record, new AnalysisCheckpoint(CandidateId, VoteDraftTask.Task, State.Running, "lease", 1,
            State.Succeeded, VoteDraftErrorCodes.FactTooLong)));
    }
}
