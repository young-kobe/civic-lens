using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Issues;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Collection.HouseVotes;

public sealed partial class PostgresHouseVoteTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    // The bill of the readable candidate has the policy area Taxation, so this issue is its one candidate.
    private static readonly IssueConfigurationRevision TaxIssues = IssueConfigurationRevision.Create(BillGateFixtures.Issues() with
    {
        Issues = [new TrackedIssueConfiguration { Id = "tax", Name = "Taxes", Definition = "Tax rates.", PolicyAreaIds = ["taxes"] }]
    });

    [Fact]
    public async Task TheTextBackfillMovesAPublishedModelTextVoteToApprovedCodeTextWithItsIssuesAndNoModelCallAndASecondRunFindsNothing()
    {
        var candidate = await ReadableCandidateAsync();
        var (recordId, codeText) = await SettleModelTextAsync(candidate, TaxIssues);
        AssertFoundNothing(await BackfillCodeTextAsync(TaxIssues));

        await PublishAsync(recordId);
        var moved = await BackfillCodeTextAsync(TaxIssues);

        Assert.Equal((1, 1, 0, null), (moved.Examined, moved.Moved, moved.Skipped, moved.NextCursor));
        Assert.Equal($"Code|Code|{codeText}|Approved", await ScalarAsync($"""
            SELECT r.author_kind || '|' || r.text_origin || '|' || r.headline || '|' || d.review_status
            FROM vote_revisions r JOIN vote_drafts d ON d.record_id = r.record_id AND d.current_revision_number = r.revision_number
            """));
        Assert.Equal("tax", await ScalarAsync("SELECT string_agg(issue_id, ',') FROM vote_revision_issues WHERE revision_number = 2 AND is_tracked"));
        Assert.Equal(ReviewAuthor.CodeSubject, await ScalarAsync("SELECT actor_subject FROM vote_review_decisions WHERE revision_number = 2"));
        Assert.Equal("Bypassed|codeWritten|0", await ScalarAsync($"""
            SELECT u.outcome || '|' || u.reason || '|' || u.charged_tokens FROM vote_analysis_runs u
            JOIN vote_revisions r ON r.run_id = u.run_id WHERE r.revision_number = 2
            """));
        Assert.Equal(1_000L, await ScalarAsync("SELECT charged_tokens FROM vote_analysis_budget WHERE task = 'vote-draft'"));
        Assert.Equal([(candidate, "vote-draft", "Succeeded", null)], await QueueAsync());

        AssertFoundNothing(await BackfillCodeTextAsync(TaxIssues));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM vote_revisions"));
    }

    [Fact]
    public async Task TheTextBackfillLeavesAVoteWhoseIssueCandidatesChangedToTheWorkerAndNamesItsReason()
    {
        var candidate = await ReadableCandidateAsync();
        var (recordId, _) = await SettleModelTextAsync(candidate, TaxIssues);
        await PublishAsync(recordId);

        var skipped = await BackfillCodeTextAsync(BillGateFixtures.Revision());

        Assert.Equal((1, 0, 1), (skipped.Examined, skipped.Moved, skipped.Skipped));
        Assert.Equal([new VoteCodeTextSkip(recordId, VoteCodeTextSkip.FactsChanged)], skipped.Skips);
        Assert.Equal("Model", await ScalarAsync("SELECT string_agg(text_origin, ',') FROM vote_revisions"));
    }

    [Fact]
    public async Task TheTextBackfillPagesByRecordIdSoACursorPassesTheVotesOfThePageBefore()
    {
        var candidate = await ReadableCandidateAsync();
        var (recordId, _) = await SettleModelTextAsync(candidate, TaxIssues);
        await PublishAsync(recordId);

        var first = await BackfillCodeTextAsync(BillGateFixtures.Revision(), limit: 1);
        var next = await BackfillCodeTextAsync(TaxIssues, limit: 1, after: first.NextCursor);

        Assert.Equal(recordId, first.NextCursor);
        AssertFoundNothing(next);
    }

    private static void AssertFoundNothing(VoteCodeTextBackfillResult result)
    {
        Assert.Equal((0, 0, 0, null), (result.Examined, result.Moved, result.Skipped, result.NextCursor));
        Assert.Empty(result.Skips);
    }

    private VoteReader Reader() => new(store, new ApiResponseReader(PostgresCollectionAttemptStore.FromConnectionString(connectionString),
        new CaptureBodyReader()), captureRoot);

    private Task<VoteCodeTextBackfillResult> BackfillCodeTextAsync(IssueConfigurationRevision issues, int limit = 10, string? after = null) =>
        new BackfillVoteCodeText(VoteStore(), Reader(), issues, TimeProvider.System).ExecuteAsync(Owner, limit, after);

    // A worker revision with model text and the same facts and issues as the code path, as published votes held before code text.
    private async Task<(string RecordId, string CodeHeadline)> SettleModelTextAsync(string candidate, IssueConfigurationRevision issues)
    {
        var evidence = (await VoteStore().ReadEvidenceAsync(candidate, default))!;
        var snapshot = (await Reader().ReadAsync(evidence, default)).Snapshot!;
        var choice = BillIssueChoice.For(snapshot.PolicyArea, snapshot.Subjects, snapshot.Day, issues.ReadConfiguration());
        var code = VoteDraftResolver.WriteByCode(new VoteDraftContext(evidence, snapshot, choice, issues.Id), Updated, ["tax"]);
        await SettleAsync(candidate, code with
        {
            AuthorSubject = ReviewAuthor.AnalysisSubject,
            TextOrigin = ReviewTextOrigin.Model,
            Headline = "The House passed the measure.",
            Summary = "The House passed the measure."
        });
        return (code.RecordId, code.Headline);
    }

    private Task PublishAsync(string recordId) => ExecuteAsync($"""
        SET session_replication_role = replica;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('vote', '{recordId}', 1, 1, NULL);
        SET session_replication_role = origin;
        """);
}
