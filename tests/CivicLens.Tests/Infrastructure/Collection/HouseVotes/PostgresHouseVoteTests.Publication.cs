using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Application.Publication;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    [Fact]
    public async Task AVoteCommitWithAStaleReviewStateVersionIsAConflictBecauseTheKindPicksTheVoteTable()
    {
        var recordId = await ApprovedVoteAsync();
        var publications = PostgresPublicationStore.FromConnectionString(connectionString);

        await Assert.ThrowsAsync<PublicationConflictException>(() =>
            publications.CommitAsync(PublicationScenario.Owner.Subject, VoteCommit(recordId, reviewStateVersion: 0), "stale", Hash(1), default));
        var committed = await publications.CommitAsync(PublicationScenario.Owner.Subject, VoteCommit(recordId, reviewStateVersion: 1), "fresh",
            Hash(2), default);

        Assert.Equal(1, committed.ReleaseNumber);
        Assert.Equal("vote", await ScalarAsync("SELECT records_json::jsonb -> 0 ->> 'kind' FROM publication_releases"));
    }

    [Fact]
    public async Task ACommittedVoteLeavesTheReadyListBecauseTheReleaseRowCarriesTheVoteKind()
    {
        var recordId = await ApprovedVoteAsync();
        var ready = Assert.Single(await VoteReviews().ListUnpublishedApprovedAsync(10, default));
        Assert.Null(ready.PublishedRevisionNumber);

        await PostgresPublicationStore.FromConnectionString(connectionString).CommitAsync(PublicationScenario.Owner.Subject,
            VoteCommit(recordId, reviewStateVersion: 1), "key", Hash(1), default);

        Assert.Empty(await VoteReviews().ListUnpublishedApprovedAsync(10, default));
    }

    [Fact]
    public async Task AModelRevisionAfterAnApprovalLeavesTheReadyListBecauseItNeedsReviewAgain()
    {
        var first = await PendingCandidateAsync();
        await SettleAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, await RevisionAsync(second, 2));

        Assert.Empty(await VoteReviews().ListUnpublishedApprovedAsync(10, default));
    }

    [Fact]
    public async Task PublicationReadReturnsTheApprovedVoteWithItsExtractionsAndTheActionRecordsOfTheSameBillOnly()
    {
        var recordId = await ApprovedVoteAsync();
        var match = new string('7', 64);
        await InsertActionDraftAsync(match, "hconres", 14);
        await InsertActionDraftAsync(new string('8', 64), "hconres", 15);
        await InsertActionDraftAsync(new string('9', 64), "hr", 14);

        var found = await VoteReviews().GetForPublicationAsync([recordId, new string('f', 64)], default);

        var read = Assert.Single(found);
        Assert.Equal(recordId, read.Key);
        Assert.Equal(ReviewStatus.Approved, read.Value.Review.CurrentStatus);
        Assert.Equal(read.Value.Review.CurrentRevision.Citations.Select(span => span.ExtractionId).Distinct().Order(),
            read.Value.Extractions.Keys.Order());
        Assert.Equal(new[] { match }, read.Value.BillActionRecordIds.ToArray());
    }

    [Fact]
    public async Task FindBillActionRecordIdsReturnsTheActionRecordsOfEachNamedBillWithoutReadingTheVoteRows()
    {
        var match = new string('7', 64);
        await InsertActionDraftAsync(match, "hconres", 14);
        await InsertActionDraftAsync(new string('9', 64), "hr", 14);

        var found = await VoteReviews().FindBillActionRecordIdsAsync(
            [new("first", 119, "hconres", 14), new("second", 119, "hr", 14), new("third", 119, "hr", 99)], default);

        Assert.Equal([match], [.. found["first"]]);
        Assert.Equal([new string('9', 64)], [.. found["second"]]);
        Assert.Empty(found["third"]);
        Assert.Empty(await VoteReviews().FindBillActionRecordIdsAsync([], default));
    }

    [Fact]
    public async Task FindBillActionRecordIdsMatchesWholeBillsNotMixesOfNamedCongressTypeAndNumber()
    {
        var wanted = new string('7', 64);
        await InsertActionDraftAsync(wanted, "hconres", 14);
        await InsertActionDraftAsync(new string('8', 64), "hconres", 15);
        await InsertActionDraftAsync(new string('9', 64), "hr", 14);
        await InsertActionDraftAsync(new string('a', 64), "hr", 15);

        var found = await VoteReviews().FindBillActionRecordIdsAsync(
            [new("first", 119, "hconres", 14), new("second", 119, "hr", 15)], default);

        Assert.Equal([wanted], [.. found["first"]]);
        Assert.Equal([new string('a', 64)], [.. found["second"]]);
    }

    [Fact]
    public async Task AVoteWithoutAnActionDraftOfItsBillGetsNoActionRecordId()
    {
        var recordId = await ApprovedVoteAsync();

        var read = (await VoteReviews().GetForPublicationAsync([recordId], default))[recordId];

        Assert.Empty(read.BillActionRecordIds);
    }

    private async Task<string> ApprovedVoteAsync()
    {
        var candidate = await PendingCandidateAsync();
        await SettleAsync(candidate, await RevisionAsync(candidate, 1));
        var recordId = await RecordIdAsync(candidate);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve);
        return recordId;
    }

    private Task InsertActionDraftAsync(string recordId, string billType, int billNumber) => ExecuteAsync($"""
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
            review_status, created_at_utc_ticks)
        VALUES ('{recordId}', 'congress-bills', 119, '{billType}', {billNumber}, 1, 0, 'AwaitingReview', 1)
        """);

    private static PublicationCommit VoteCommit(string recordId, int reviewStateVersion) =>
        new(1, null, $"000001-{Guid.NewGuid():N}", [new(PublishedRecordKind.Vote, recordId, 1)],
            [new(PublishedRecordKind.Vote, recordId, 1, reviewStateVersion)]);

    private static string Hash(int seed) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(seed)));
}
