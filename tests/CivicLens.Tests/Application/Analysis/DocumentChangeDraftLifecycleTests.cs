using CivicLens.Application.Analysis;
using CivicLens.Core.Review;
using State = CivicLens.Application.Analysis.AnalysisState;

namespace CivicLens.Tests.Application.Analysis;

public sealed class DocumentChangeDraftLifecycleTests
{
    private const string ComparisonId = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

    private static readonly HashSet<(State From, State To)> Allowed =
    [
        (State.Pending, State.Running),
        (State.RetryWaiting, State.Running),
        (State.WaitingForBudget, State.Running),
        (State.WaitingForBudget, State.Pending),
        (State.Failed, State.Pending),
        (State.Running, State.Running),
        (State.Running, State.Pending),
        (State.Running, State.RetryWaiting),
        (State.Running, State.WaitingForBudget),
        (State.Running, State.Succeeded),
        (State.Running, State.Blocked),
        (State.Running, State.Failed)
    ];

    [Fact]
    public void OnlyTheListedMovesAreValid()
    {
        foreach (var from in Enum.GetValues<State>())
            foreach (var to in Enum.GetValues<State>())
                Assert.True(Allowed.Contains((from, to)) == DocumentChangeDraftLifecycle.CanTransition(Record(from), Move(from, to)),
                    $"{from} to {to}");
    }

    [Fact]
    public void AStaleLeaseFenceOrStatusCannotMoveTheEntry()
    {
        var record = Record(State.Running);
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record, Move(State.Running, State.Failed) with { LeaseToken = "stale" }));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record, Move(State.Running, State.Failed) with { Fence = 2 }));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record, Move(State.Running, State.Failed) with { ExpectedStatus = State.Pending }));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record with { LeaseToken = null }, Move(State.Running, State.Failed) with { LeaseToken = null }));
    }

    [Fact]
    public void EachTargetCarriesOnlyItsOwnOutcome()
    {
        var record = Record(State.Running);
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record, Move(State.Running, State.Failed) with { ErrorCode = null }));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record, Move(State.Running, State.Failed) with { ErrorCode = "  " }));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record, Move(State.Running, State.RetryWaiting) with { RetryDelay = null }));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record,
            Move(State.Running, State.RetryWaiting) with { RetryDelay = TimeSpan.FromHours(1) }));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record, Move(State.Running, State.Pending) with { ErrorCode = "x" }));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record, Move(State.Running, State.Failed) with { Revision = Revision(ComparisonId) }));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record, Move(State.Running, State.Succeeded) with { ErrorCode = null }));
        Assert.False(DocumentChangeDraftLifecycle.CanTransition(record,
            Move(State.Running, State.Succeeded) with { ErrorCode = null, Revision = Revision(new string('e', 64)) }));
        Assert.True(DocumentChangeDraftLifecycle.CanTransition(record,
            Move(State.Running, State.Succeeded) with { ErrorCode = null, Revision = Revision(ComparisonId) }));
    }

    private static DocumentChangeAnalysisRecord Record(State status) => new(ComparisonId, "draft", status, "lease", 1, 0, null, null, null);

    private static DocumentChangeDraftCheckpoint Move(State from, State to) => new(ComparisonId, "draft", from, "lease", 1, to,
        to switch
        {
            State.RetryWaiting or State.WaitingForBudget or State.Blocked or State.Failed => "reason",
            State.Succeeded => "draftDiscarded",
            _ => null
        },
        to == State.RetryWaiting ? TimeSpan.FromSeconds(30) : null);

    private static DocumentChangeDraftRevision Revision(string comparisonId) => new(new string('d', 32), 1, comparisonId,
        ReviewAuthor.AnalysisSubject, DateTimeOffset.UnixEpoch, "Headline", "Summary", null, "Limits", "Office", null, null, [], [], []);
}
