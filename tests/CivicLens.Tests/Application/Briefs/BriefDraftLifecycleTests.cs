using CivicLens.Application.Analysis;
using CivicLens.Application.Briefs;
using CivicLens.Tests.Fixtures;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Briefs;

public sealed class BriefDraftLifecycleTests
{
    [Theory]
    [InlineData(State.Succeeded)]
    [InlineData(State.Blocked)]
    [InlineData(State.Failed)]
    public void AFinishedWeekMayReturnToPendingSoTheOwnerCanRedraftItAndAPublicationCanMakeItStale(State finished)
    {
        var record = new AnalysisRecord(BriefFixtures.RecordId, BriefDraftTask.Task, finished, null, 1, 1, null, null);
        var requeue = new AnalysisCheckpoint(BriefFixtures.RecordId, BriefDraftTask.Task, finished, null, 1, State.Pending);

        Assert.True(BriefDraftLifecycle.CanTransition(record, requeue));
    }

    [Theory]
    [InlineData(BriefDraftErrorCodes.NoNewRecords)]
    [InlineData(BriefDraftErrorCodes.Unchanged)]
    public void ARunningWeekMaySettleSucceededWithoutARevisionOnlyForTheTwoBypassReasons(string reason)
    {
        var running = new AnalysisRecord(BriefFixtures.RecordId, BriefDraftTask.Task, State.Running, "lease", 1, 0, null, null);
        var settle = new AnalysisCheckpoint(BriefFixtures.RecordId, BriefDraftTask.Task, State.Running, "lease", 1, State.Succeeded, reason);
        var unknown = settle with { ErrorCode = BriefDraftErrorCodes.OverByteCap };

        Assert.True(BriefDraftLifecycle.CanTransition(running, settle));
        Assert.False(BriefDraftLifecycle.CanTransition(running, unknown));
    }

    [Fact]
    public void ARevisionSettlesOnlyForItsOwnWeekWithNoErrorCode()
    {
        var running = new AnalysisRecord(BriefFixtures.RecordId, BriefDraftTask.Task, State.Running, "lease", 1, 0, null, null);
        var claim = new AnalysisClaim(running, "lease", 1);
        var own = BriefDraftCheckpoint.From(claim, BriefFixtures.Revision());
        var other = BriefDraftCheckpoint.From(claim, BriefFixtures.Revision() with { RecordId = new string('c', 64) });

        Assert.True(BriefDraftLifecycle.CanTransition(running, own));
        Assert.False(BriefDraftLifecycle.CanTransition(running, other));
        Assert.False(BriefDraftLifecycle.CanTransition(running, own with { ErrorCode = BriefDraftErrorCodes.Unchanged }));
    }
}
