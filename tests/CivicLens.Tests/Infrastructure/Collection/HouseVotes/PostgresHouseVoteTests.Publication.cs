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
        Assert.Equal("vote", await ScalarAsync("SELECT kind FROM publication_record_states"));
    }

    [Fact]
    public async Task ACommittedVoteLeavesTheReadyListBecauseTheReleaseRowCarriesTheVoteKind()
    {
        var recordId = await ApprovedVoteAsync();
        var ready = Assert.Single(await VoteReviews().ListUnpublishedApprovedAsync(10, [], default));

        await PostgresPublicationStore.FromConnectionString(connectionString).CommitAsync(PublicationScenario.Owner.Subject,
            VoteCommit(recordId, reviewStateVersion: 1), "key", Hash(1), default);

        Assert.Empty(await VoteReviews().ListUnpublishedApprovedAsync(10, [], default));
    }

    [Fact]
    public async Task AModelRevisionAfterAnApprovalLeavesTheReadyListBecauseItNeedsReviewAgain()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleForReviewAsync(second, await RevisionAsync(second, 2));

        Assert.Empty(await VoteReviews().ListUnpublishedApprovedAsync(10, [], default));
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
    public async Task PublicationReadHoldsOnlyTheLastApprovedAndTheCurrentRevisionSoTheReplacedPublishedRevisionComesFromTheReleaseFile()
    {
        var first = await PendingCandidateAsync();
        await SettleAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(second, await RevisionAsync(second, 2));
        var third = await PendingCandidateAsync(Updated.AddDays(2));
        await SettleAsync(third, await RevisionAsync(third, 3));

        var review = (await VoteReviews().GetForPublicationAsync([recordId], default))[recordId].Review;

        Assert.Equal([3], review.Revisions.Select(revision => revision.RevisionNumber));
    }

    [Fact]
    public async Task FindBillActionRecordIdsOfVotesReturnsTheActionRecordsOnTheBillOfEachNamedVoteRevisionInOneQuery()
    {
        var recordId = await ApprovedVoteAsync();
        var sameBill = new string('7', 64);
        await InsertActionDraftAsync(sameBill, "hconres", 14);
        await InsertActionDraftAsync(new string('9', 64), "hr", 14);

        var found = await VoteReviews().FindBillActionRecordIdsOfVotesAsync([new(PublishedRecordKind.Vote, recordId, 1)], default);

        Assert.Equal([sameBill], [.. found[recordId]]);
        Assert.Empty(await VoteReviews().FindBillActionRecordIdsOfVotesAsync([], default));
    }

    [Fact]
    public async Task FindBillActionRecordIdsOfVotesUsesTheBillOfThePublishedRevisionAndNotTheBillOfTheCurrentCandidate()
    {
        var recordId = await ApprovedVoteAsync();
        var publishedBill = new string('7', 64);
        var newerBill = new string('8', 64);
        await InsertActionDraftAsync(publishedBill, "hconres", 14);
        await InsertActionDraftAsync(newerBill, "hr", 99);
        var newer = new string('1', 64);
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO vote_candidates (candidate_id, source_id, chamber, congress, session_number, roll_call_number, update_date_utc_ticks,
                bill_type, bill_number, detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
            SELECT '{newer}', source_id, chamber, congress, session_number, roll_call_number, update_date_utc_ticks + 1,
                'hr', 99, 'newer-detail', 'newer-members', detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks
            FROM vote_candidates WHERE candidate_id = (SELECT candidate_id FROM vote_drafts WHERE record_id = '{recordId}');
            UPDATE vote_drafts SET candidate_id = '{newer}' WHERE record_id = '{recordId}';
            """);

        var found = await VoteReviews().FindBillActionRecordIdsOfVotesAsync([new(PublishedRecordKind.Vote, recordId, 1)], default);

        Assert.Equal([publishedBill], [.. found[recordId]]);
    }

    [Fact]
    public async Task FindVoteRecordIdsOnBillsOfActionsReturnsTheVotesOnTheBillOfEachNamedActionOnly()
    {
        var recordId = await ApprovedVoteAsync();
        var sameBill = new string('7', 64);
        var otherBill = new string('9', 64);
        await InsertActionDraftAsync(sameBill, "hconres", 14);
        await InsertActionDraftAsync(otherBill, "hr", 14);

        Assert.Equal([recordId], [.. await VoteReviews().FindVoteRecordIdsOnBillsOfActionsAsync([sameBill, otherBill], default)]);
        Assert.Empty(await VoteReviews().FindVoteRecordIdsOnBillsOfActionsAsync([otherBill], default));
        Assert.Empty(await VoteReviews().FindVoteRecordIdsOnBillsOfActionsAsync([], default));
    }

    [Fact]
    public async Task ReadMembersReturnsTheRecordAndPersonOfEachMemberOfTheNamedRevisionsInOneQueryAndRefusesARevisionThatIsNotStored()
    {
        var recordId = await ApprovedVoteAsync();
        var stored = await VoteReviews().GetRevisionAsync(recordId, 1, default);

        var members = await VoteReviews().ReadMembersAsync([new(PublishedRecordKind.Vote, recordId, 1)], default);

        Assert.NotEmpty(stored!.OfficialIds());
        Assert.Equal(stored.OfficialIds().Order(), members.Select(member => member.PersonId).Order());
        Assert.All(members, member => Assert.Equal(recordId, member.RecordId));
        Assert.Empty(await VoteReviews().ReadMembersAsync([], default));
        await Assert.ThrowsAsync<InvalidDataException>(() => VoteReviews().ReadMembersAsync([new(PublishedRecordKind.Vote, recordId, 2)], default));
    }

    [Fact]
    public async Task AVoteRevisionThatIsNotNamedIsNotReadBecauseTheReleaseBindsToTheExactRevision()
    {
        var first = await PendingCandidateAsync();
        await SettleForReviewAsync(first, await RevisionAsync(first, 1));
        var recordId = await RecordIdAsync(first);
        var second = await PendingCandidateAsync(Updated.AddDays(1));
        var newer = await RevisionAsync(second, 2);
        await SettleForReviewAsync(second, newer with { Members = [newer.Members[0] with { PersonId = "other-member" }] });

        var ids = (await VoteReviews().ReadMembersAsync([new(PublishedRecordKind.Vote, recordId, 1)], default))
            .Select(member => member.PersonId).ToArray();

        Assert.Equal(["other-member"], [.. (await VoteReviews().GetRevisionAsync(recordId, 2, default))!.OfficialIds()]);
        Assert.Equal([.. (await VoteReviews().GetRevisionAsync(recordId, 1, default))!.OfficialIds()], ids);
        Assert.DoesNotContain("other-member", ids);
    }

    [Fact]
    public async Task ARevisionOfAnotherKindIsRefusedBeforeAnyQueryBecauseAMixedCallWouldReadTheWrongTable()
    {
        var recordId = await ApprovedVoteAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => VoteReviews().ReadMembersAsync(
            [new(PublishedRecordKind.Vote, recordId, 1), new(PublishedRecordKind.Statement, recordId, 1)], default));
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
        await SettleForReviewAsync(candidate, await RevisionAsync(candidate, 1));
        var recordId = await RecordIdAsync(candidate);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve);
        return recordId;
    }

    private Task InsertActionDraftAsync(string recordId, string billType, int billNumber) => ExecuteAsync($"""
        INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, review_state_version,
            review_status, created_at_utc_ticks, last_revised_at_utc_ticks)
        VALUES ('{recordId}', 'congress-bills', 119, '{billType}', {billNumber}, 1, 0, 'AwaitingReview', 1, 1)
        """);

    private static PublicationCommit VoteCommit(string recordId, int reviewStateVersion) =>
        new(1, null, $"000001-{Guid.NewGuid():N}", [new(PublishedRecordKind.Vote, recordId, 1)],
            [new(PublishedRecordKind.Vote, recordId, 1, reviewStateVersion)]);

    private static string Hash(int seed) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(seed)));
}
