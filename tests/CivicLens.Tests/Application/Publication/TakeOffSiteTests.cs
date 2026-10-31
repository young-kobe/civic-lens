using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class TakeOffSiteTests
{
    private const string FormKey = "form-key";

    [Fact]
    public async Task ARetryAfterTheWithdrawalStepFailedRecordsNoSecondDecisionBecauseTheRecordIsAlreadyBlockedAndTheKeyIsTheSame()
    {
        var (scenario, action) = await PublishedActionAsync();
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();
        var takeOff = TakeOff(scenario);
        scenario.Publications.ConflictOnCommit = true;

        var failed = await takeOff.ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.Approved), Record(decisions));

        Assert.Contains("Review state changed.", failed, StringComparison.Ordinal);
        Assert.Contains("The record is still on the public site.", failed, StringComparison.Ordinal);
        Assert.Contains("Your decision on the record is saved.", failed, StringComparison.Ordinal);
        scenario.Publications.ConflictOnCommit = false;

        var retried = await takeOff.ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.ApprovalWithdrawn), Record(decisions));

        Assert.Null(retried);
        Assert.Equal([(ReviewDecisionKind.WithdrawApproval, FormKey + ":decision")], decisions);
        Assert.Equal(action, Assert.Single(Manifest(scenario).Withdrawals).RecordId);
    }

    [Fact]
    public async Task ARetryFromAStalePageSendsTheSameDecisionKeySoTheStoreReplaysTheFirstDecision()
    {
        var (scenario, action) = await PublishedActionAsync();
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();
        var takeOff = TakeOff(scenario);
        scenario.Publications.ConflictOnCommit = true;
        await takeOff.ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.Approved), Record(decisions));
        scenario.Publications.ConflictOnCommit = false;

        await takeOff.ExecuteAsync(PublicationScenario.Owner, Request(action, ReviewStatus.Approved), Record(decisions));

        Assert.Single(decisions.Select(decision => decision.Key).Distinct());
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
            TakeOff(scenario).IsOnSiteAsync(PublicationScenario.Reviewer, new(PublishedRecordKind.Action, action)));

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

        Assert.Equal("This record is not on the public site.", refusal);
        Assert.Empty(decisions);
    }

    [Fact]
    public async Task ANoteOutsideTheLimitsWritesNoDecisionBecauseTheWithdrawalWouldRefuseItAfterwards()
    {
        var (scenario, action) = await PublishedActionAsync();
        var decisions = new List<(ReviewDecisionKind Kind, string Key)>();

        await Assert.ThrowsAsync<ArgumentException>(() => TakeOff(scenario).ExecuteAsync(PublicationScenario.Owner,
            Request(action, ReviewStatus.Approved) with { Note = " " }, Record(decisions)));

        Assert.Empty(decisions);
    }

    private static TakeOffSite TakeOff(PublicationScenario scenario) =>
        new(scenario.Publisher(), scenario.Publications, scenario.Releases);

    private static TakeOffSiteRequest Request(string id, ReviewStatus status) =>
        new(new(PublishedRecordKind.Action, id), status, "Wrong bill.", FormKey);

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
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k1", new PublishedRecordReference(PublishedRecordKind.Action, action)));
        return (scenario, action);
    }

    private static PublicationRelease Manifest(PublicationScenario scenario)
    {
        var directory = scenario.Publications.Committed.Last().DirectoryName;
        return JsonSerializer.Deserialize<PublicationRelease>(scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath],
            PublicationProtocol.JsonOptions)!;
    }
}
