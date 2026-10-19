using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    [Fact]
    public async Task BulkApprovalApprovesAProceduralFirstDraftBecauseItsKindAloneIsWhatTheReviewerChecks()
    {
        var recordId = await SettleProceduralFirstDraftAsync();

        var results = await BulkApproveAsync(new BulkApprovalTarget(recordId, 1, 0));

        Assert.Equal(new BulkApprovalResult(recordId, BulkApprovalOutcome.Approved), Assert.Single(results));
        Assert.Equal("Approved", await ScalarAsync($"SELECT review_status FROM vote_drafts WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task BulkApprovalRejectsAFirstDraftThatIsNotProceduralAndWritesNoDecision()
    {
        var candidate = await PendingCandidateAsync();
        await SettleAsync(candidate, await RevisionAsync(candidate, 1));
        var recordId = await RecordIdAsync(candidate);

        var results = await BulkApproveAsync(new BulkApprovalTarget(recordId, 1, 0));

        Assert.Equal(BulkApprovalOutcome.Rejected, Assert.Single(results).Outcome);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM vote_review_decisions"));
    }

    [Fact]
    public async Task BulkApprovalApprovesARevisionThatChangesOnlyMemberVotesAndRejectsOneWhoseTextChanged()
    {
        var older = await PendingCandidateAsync();
        var first = await RevisionAsync(older, 1);
        await SettleAsync(older, first);
        var recordId = await RecordIdAsync(older);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve);
        var newer = await PendingCandidateAsync(Updated.AddDays(1));
        var changedMembers = await RevisionAsync(newer, 2) with { Members = [first.Members[0] with { Cast = "Nay" }] };
        await SettleAsync(newer, changedMembers);

        var approved = await BulkApproveAsync(new BulkApprovalTarget(recordId, 2, 1));

        Assert.Equal(BulkApprovalOutcome.Approved, Assert.Single(approved).Outcome);
        Assert.Equal(2, await ScalarAsync("SELECT revision_number FROM vote_review_decisions WHERE review_state_version = 2"));
    }

    [Fact]
    public async Task BulkApprovalRejectsAMemberVoteChangeThatCameWithNewTextBecauseTheApprovalDoesNotCoverTheText()
    {
        var older = await PendingCandidateAsync();
        await SettleAsync(older, await RevisionAsync(older, 1));
        var recordId = await RecordIdAsync(older);
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve);
        var newer = await PendingCandidateAsync(Updated.AddDays(1));
        await SettleAsync(newer, await RevisionAsync(newer, 2) with { Headline = "New headline", Summary = "New summary." });

        var results = await BulkApproveAsync(new BulkApprovalTarget(recordId, 2, 1));

        Assert.Equal(BulkApprovalOutcome.Rejected, Assert.Single(results).Outcome);
        Assert.Equal("AwaitingReview", await ScalarAsync($"SELECT review_status FROM vote_drafts WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task BulkApprovalRefusesAProceduralVoteWithAnOpenConcernBecauseItWritesNoResolutionNote()
    {
        var recordId = await SettleProceduralFirstDraftAsync();
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.RequestChanges, "Check the kind.");

        var results = await BulkApproveAsync(new BulkApprovalTarget(recordId, 1, 1));

        Assert.Equal(BulkApprovalOutcome.Rejected, Assert.Single(results).Outcome);
        Assert.Equal("ChangesRequested", await ScalarAsync($"SELECT review_status FROM vote_drafts WHERE record_id = '{recordId}'"));
    }

    [Fact]
    public async Task BulkApprovalBindsToTheExactRevisionSoANewerRevisionTurnsTheTargetIntoAConflict()
    {
        var recordId = await SettleProceduralFirstDraftAsync();

        var results = await BulkApproveAsync(new BulkApprovalTarget(recordId, 1, 5), new BulkApprovalTarget(new string('9', 64), 1, 0));

        Assert.Equal([BulkApprovalOutcome.Conflict, BulkApprovalOutcome.NotFound], results.Select(result => result.Outcome));
    }

    [Fact]
    public async Task TriageListsOnlyVotesThatNeedActionWithTheirExtractionsAndAPassingApprovalCheck()
    {
        var recordId = await SettleProceduralFirstDraftAsync();
        var triage = new ListVoteTriage(VoteReviews());

        var page = await triage.ExecuteAsync(Reviewer, null, new(DraftStatusFilter.Approved));

        var row = Assert.Single(page.Rows);
        Assert.Equal(recordId, row.Review.RecordId);
        Assert.True(row.Check.Passed);
        Assert.NotEmpty(row.Quotes);
        Assert.All(row.Quotes, quote => Assert.NotEmpty(quote.Text));
        await DecideAsync(recordId, 1, 0, ReviewDecisionKind.Approve);
        Assert.Empty((await triage.ExecuteAsync(Reviewer, null)).Rows);
    }

    private async Task<string> SettleProceduralFirstDraftAsync()
    {
        var candidate = await PendingCandidateAsync();
        var revision = await RevisionAsync(candidate, 1);
        await SettleAsync(candidate, revision with { Kind = VoteKind.Rule, KindCitation = revision.VotedOnCitation });
        return await RecordIdAsync(candidate);
    }

    private Task<ImmutableArray<BulkApprovalResult>> BulkApproveAsync(params BulkApprovalTarget[] targets) =>
        new ApproveBulkSafeVotes(VoteReviews()).ExecuteAsync(Reviewer, new([.. targets], Guid.NewGuid().ToString("N")));
}
