using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Application.Publication;

public sealed class TakeOffSiteTests
{
    private const string FormKey = "form-key";

    [Fact]
    public async Task ARetryAfterTheWithdrawalStepFailedRecordsNoSecondDecisionBecauseTheRecordIsAlreadyBlocked()
    {
        var (scenario, action) = await PublishedActionAsync();
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();
        var takeOff = TakeOff(scenario);
        scenario.Publications.ConflictOnCommit = true;

        var failed = await takeOff.ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.Approved), Record(decisions));

        Assert.Equal("Another release went out first, so the public site still shows the record. Your decision on the record is saved. Press Take off site again.", failed);
        scenario.Publications.ConflictOnCommit = false;

        var retried = await takeOff.ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.ApprovalWithdrawn), Record(decisions));

        Assert.Null(retried);
        Assert.Equal([(ReviewDecisionKind.WithdrawApproval, FormKey + ":decision")], decisions);
        Assert.Equal(action, Assert.Single(Manifest(scenario).Withdrawals).RecordId);
    }

    [Fact]
    public async Task ARetryAfterTheServeFailedServesTheCommittedReleaseAgainInsteadOfSayingTheRecordIsNotOnTheSite()
    {
        var (scenario, action) = await PublishedActionAsync();
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();
        var takeOff = TakeOff(scenario);
        scenario.Releases.FailOnActivate = new IOException("The link could not move.");

        var failed = await takeOff.ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.Approved), Record(decisions));

        Assert.Equal("The public site could not be updated. Your decision on the record is saved. Press Take off site again. If this repeats, ask the operator to check the release directory.", failed);

        var pending = await takeOff.GetStatusAsync(PublicationScenario.Owner, Reference(action));
        Assert.Equal((false, true, true, true), (pending.OnSite, pending.TakenOff, pending.ReadersSeeOldSite, pending.CanTakeOff));
        scenario.Releases.FailOnActivate = null;

        var retried = await takeOff.ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.ApprovalWithdrawn), Record(decisions));

        Assert.Null(retried);
        Assert.Equal(scenario.Publications.Committed.Last().DirectoryName, scenario.Releases.Active);
        Assert.Single(decisions);
        var served = await takeOff.GetStatusAsync(PublicationScenario.Owner, Reference(action));
        Assert.False(served.CanTakeOff);
        Assert.True(served.TakenOff);
    }

    [Fact]
    public async Task ACitedRecordIsRefusedBeforeAnyDecisionAndTheSentenceNamesTheCitingRecord()
    {
        var scenario = new PublicationScenario();
        var statement = scenario.AddStatement('b');
        var action = scenario.AddAction('d');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement), Reference(action)));
        var relationship = scenario.AddRelationship('e', statement, action);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2",
            new PublishedRecordReference(PublishedRecordKind.Relationship, relationship)));
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();
        var headline = Manifest(scenario).Records.Single(entry => entry.RecordId == relationship).Headline;

        var refusal = await TakeOff(scenario).ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.Approved), Record(decisions));

        Assert.Equal($"The public site still shows records that cite this one: \"{headline}\" (relationship:{relationship}). No decision was written. Take those records off first. Then take this record off.", refusal);
        Assert.Empty(decisions);
    }

    [Theory]
    [InlineData(ReviewStatus.Approved, ReviewDecisionKind.WithdrawApproval)]
    [InlineData(ReviewStatus.AwaitingReview, ReviewDecisionKind.RequestChanges)]
    public async Task AnUnblockedRecordGetsTheDecisionThatBlocksItBecauseOnlyABlockedRecordCanLeaveTheSite(ReviewStatus status, ReviewDecisionKind expected)
    {
        var (scenario, action) = await PublishedActionAsync();
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();

        var refusal = await TakeOff(scenario).ExecuteAsync(PublicationScenario.Owner, Request(action, status), Record(decisions));

        Assert.Null(refusal);
        Assert.Equal(expected, Assert.Single(decisions).Kind);
    }

    [Theory]
    [InlineData(ReviewStatus.ApprovalWithdrawn)]
    [InlineData(ReviewStatus.ChangesRequested)]
    [InlineData(ReviewStatus.AwaitingReviewWithConcerns)]
    public async Task ABlockedRecordRecordsNoNewDecisionBecauseReviewAlreadyBlocksIt(ReviewStatus status)
    {
        var (scenario, action) = await PublishedActionAsync();
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();

        var refusal = await TakeOff(scenario).ExecuteAsync(PublicationScenario.Owner, Request(action, status), Record(decisions));

        Assert.Null(refusal);
        Assert.Empty(decisions);
    }

    [Fact]
    public async Task AReviewerIsRefusedBeforeAnyDecisionBecauseOnlyTheOwnerTakesRecordsOffTheSite()
    {
        var (scenario, action) = await PublishedActionAsync();
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            TakeOff(scenario).ExecuteAsync(PublicationScenario.Reviewer, Request(action, ReviewStatus.Approved), Record(decisions)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            TakeOff(scenario).GetStatusAsync(PublicationScenario.Reviewer, Reference(action)));

        Assert.Empty(decisions);
        Assert.Empty(Manifest(scenario).Withdrawals);
    }

    [Fact]
    public async Task ARecordThatIsNotOnTheSiteWritesNoDecisionBecauseTheWithdrawalCouldNeverFollow()
    {
        var (scenario, _) = await PublishedActionAsync();
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();
        var unpublished = scenario.AddAction('d');

        var refusal = await TakeOff(scenario).ExecuteAsync(PublicationScenario.Owner, Request(unpublished, ReviewStatus.Approved), Record(decisions));

        Assert.Equal("This record is not on the public site, so there is nothing to take off. Reload the page to see its current state.", refusal);
        Assert.Empty(decisions);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    public async Task AMissingReasonWritesNoDecisionAndTellsTheOwnerTheLimits(string note)
    {
        var (scenario, action) = await PublishedActionAsync();
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();

        var refusal = await TakeOff(scenario).ExecuteAsync(PublicationScenario.Owner,
            Request(action, ReviewStatus.Approved) with { Note = note }, Record(decisions));

        Assert.Equal("Type a reason of 1 to 500 characters.", refusal);
        Assert.Empty(decisions);
    }

    [Fact]
    public async Task AnEmptyFormKeyIsInvalidBeforeAnyWorkBecauseTheDerivedKeysWouldHideIt()
    {
        var (scenario, action) = await PublishedActionAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => TakeOff(scenario).ExecuteAsync(PublicationScenario.Owner,
            Request(action, ReviewStatus.Approved) with { IdempotencyKey = "" }, Record([])));
    }

    [Fact]
    public async Task AFailedDecisionStepTellsTheOwnerNothingChangedAndPublishesNothing()
    {
        var (scenario, action) = await PublishedActionAsync();

        var refusal = await TakeOff(scenario).ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.Approved),
            (_, _) => throw new ArgumentException("Raw detail that the owner must not see."));

        Assert.Equal("The decision could not be recorded, so the public site is unchanged. Reload the page, check the review state, and try again.", refusal);
        Assert.Empty(Manifest(scenario).Withdrawals);
    }

    [Fact]
    public async Task ARetryWithAnotherReasonAfterTheCommitGetsATrueSentenceBecauseTheKeyAlreadyWentOutWithTheFirstReason()
    {
        var (scenario, action) = await PublishedActionAsync();
        var takeOff = TakeOff(scenario);
        scenario.Releases.FailOnActivate = new IOException("The link could not move.");
        await takeOff.ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.Approved), Record([]));
        scenario.Releases.FailOnActivate = null;

        var refusal = await takeOff.ExecuteAsync(PublicationScenario.Owner,
            Request(action, ReviewStatus.ApprovalWithdrawn) with { Note = "Another reason." }, Record([]));

        Assert.Equal("This request already went out with a different reason. Type the first reason again, or reload the page.", refusal);
    }

    [Fact]
    public async Task WhileTheActiveReleaseIsNotTheLatestTheStatusWarnsOfTheRollbackPauseButStillOffersTheAction()
    {
        var (scenario, action) = await PublishedActionAsync();
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b'))));
        scenario.Publications.ActiveReleaseNumber = 1;

        var status = await TakeOff(scenario).GetStatusAsync(PublicationScenario.Owner, Reference(action));

        Assert.Equal((true, true), (status.RolledBack, status.CanTakeOff));
    }

    [Fact]
    public async Task AWithdrawnRecordIsReportedAsTakenOffSoThePanelCanSayHowItReturns()
    {
        var (scenario, action) = await PublishedActionAsync();
        var takeOff = TakeOff(scenario);
        await takeOff.ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.ApprovalWithdrawn), Record([]));

        var status = await takeOff.GetStatusAsync(PublicationScenario.Owner, Reference(action));

        Assert.Equal((false, true, false), (status.OnSite, status.TakenOff, status.CanTakeOff));
    }

    private static TakeOffSite TakeOff(PublicationScenario scenario) =>
        new(scenario.Publisher(), scenario.Publications, scenario.Releases, scenario.Loaders(), NullLogger<TakeOffSite>.Instance);

    private static PublishedRecordReference Reference(string id) => new(PublishedRecordKind.Action, id);

    private static TakeOffSiteRequest Request(string id, ReviewStatus status) => new(Reference(id), status, "Wrong bill.", FormKey);

    private static Func<ReviewDecisionKind, string, Task> Record(List<(ReviewDecisionKind Kind, string Key)> decisions) =>
        (kind, key) =>
        {
            decisions.Add((kind, key));
            return Task.CompletedTask;
        };

    private static async Task<(PublicationScenario Scenario, string Action)> PublishedActionAsync()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Reference(action)));
        return (scenario, action);
    }

    private static PublicationRelease Manifest(PublicationScenario scenario)
    {
        var directory = scenario.Publications.Committed.Last().DirectoryName;
        return JsonSerializer.Deserialize<PublicationRelease>(scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath],
            PublicationProtocol.JsonOptions)!;
    }
}
