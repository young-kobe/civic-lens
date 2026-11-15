using CivicLens.Application.Actions;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Issues;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Application.Actions;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    public enum RetiredText { CrsRewrite, TitleOnlyDraft, IntroducedByHeadline }

    [Theory]
    [InlineData(RetiredText.CrsRewrite)]
    [InlineData(RetiredText.TitleOnlyDraft)]
    [InlineData(RetiredText.IntroducedByHeadline)]
    public async Task TheTextBackfillMovesAPublishedRetiredRecordToApprovedCodeTextWithItsIssuesAndNoModelCallAndASecondRunFindsNothing(RetiredText kind)
    {
        var candidate = await ReadableCandidateAsync("A000001", withSummary: kind != RetiredText.TitleOnlyDraft);
        var codeHeadline = await SettleRetiredTextAsync(candidate, kind);
        AssertFoundNothing(await BackfillCodeTextAsync(BillGateFixtures.Revision()));
        var chargedBefore = await ScalarAsync("SELECT coalesce(sum(charged_tokens), 0) FROM action_analysis_budget");

        await PublishActionAsync();
        var moved = await BackfillCodeTextAsync(BillGateFixtures.Revision());

        Assert.Equal((1, 1, 0, null), (moved.Examined, moved.Moved, moved.Skipped, moved.NextCursor));
        Assert.Equal($"Code|Code|{codeHeadline}|Approved", await ScalarAsync("""
            SELECT r.author_kind || '|' || r.text_origin || '|' || r.headline || '|' || d.review_status
            FROM action_revisions r JOIN action_drafts d ON d.record_id = r.record_id AND d.current_revision_number = r.revision_number
            """));
        Assert.Equal("rent", await ScalarAsync("SELECT string_agg(issue_id, ',') FROM action_revision_issues WHERE revision_number = 2 AND is_tracked"));
        Assert.Equal(ReviewAuthor.CodeSubject, await ScalarAsync("SELECT actor_subject FROM action_review_decisions WHERE revision_number = 2"));
        Assert.Equal("Bypassed|codeWritten|0", await ScalarAsync("""
            SELECT u.outcome || '|' || u.reason || '|' || u.charged_tokens FROM action_analysis_runs u
            JOIN action_revisions r ON r.run_id = u.run_id WHERE r.revision_number = 2
            """));
        Assert.Equal(chargedBefore, await ScalarAsync("SELECT coalesce(sum(charged_tokens), 0) FROM action_analysis_budget"));
        Assert.Equal("Succeeded", Assert.Single(await QueueAsync()).Status);

        AssertFoundNothing(await BackfillCodeTextAsync(BillGateFixtures.Revision()));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM action_revisions"));
    }

    [Fact]
    public async Task TheTextBackfillLeavesARecordWhoseCodeTextIsCurrentAlone()
    {
        var candidate = await ReadableCandidateAsync("A000001");
        await SettleRetiredTextAsync(candidate, RetiredText.IntroducedByHeadline, (_, code) => code);
        await PublishActionAsync();

        AssertFoundNothing(await BackfillCodeTextAsync(BillGateFixtures.Revision()));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions"));
    }

    [Fact]
    public async Task TheTextBackfillLeavesARecordThatIsNoLongerPublishedAlone()
    {
        var candidate = await ReadableCandidateAsync("A000001");
        await SettleRetiredTextAsync(candidate, RetiredText.CrsRewrite);
        await PublishActionAsync(lastRelease: "1");

        AssertFoundNothing(await BackfillCodeTextAsync(BillGateFixtures.Revision()));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions"));
    }

    [Fact]
    public async Task TheTextBackfillLeavesARecordWhoseIssueCandidatesChangedToTheWorkerAndNamesItsReason()
    {
        var candidate = await ReadableCandidateAsync("A000001");
        await SettleRetiredTextAsync(candidate, RetiredText.CrsRewrite);
        await PublishActionAsync();

        var skipped = await BackfillCodeTextAsync(BillGateFixtures.Revision(withEviction: true));

        Assert.Equal((1, 0, 1), (skipped.Examined, skipped.Moved, skipped.Skipped));
        Assert.Equal([new ActionCodeTextSkip(RecordId, ActionCodeTextSkip.FactsChanged)], skipped.Skips);
        Assert.Equal("Model", await ScalarAsync("SELECT string_agg(text_origin, ',') FROM action_revisions"));
    }

    [Fact]
    public async Task TheTextBackfillPagesByRecordIdSoACursorPassesTheRecordsOfThePageBefore()
    {
        var candidate = await ReadableCandidateAsync("A000001");
        await SettleRetiredTextAsync(candidate, RetiredText.CrsRewrite);
        await PublishActionAsync();

        var first = await BackfillCodeTextAsync(BillGateFixtures.Revision(withEviction: true), limit: 1);
        var next = await BackfillCodeTextAsync(BillGateFixtures.Revision(), limit: 1, after: first.NextCursor);

        Assert.Equal(RecordId, first.NextCursor);
        AssertFoundNothing(next);
    }

    private static void AssertFoundNothing(ActionCodeTextBackfillResult result)
    {
        Assert.Equal((0, 0, 0, null), (result.Examined, result.Moved, result.Skipped, result.NextCursor));
        Assert.Empty(result.Skips);
    }

    private ActionBillReader BackfillReader() => new(store, new ApiResponseReader(PostgresCollectionAttemptStore.FromConnectionString(connectionString),
        new CaptureBodyReader()), new ActionBillWorld.FragmentExtractor(), captureRoot);

    private Task<ActionCodeTextBackfillResult> BackfillCodeTextAsync(IssueConfigurationRevision issues, int limit = 10, string? after = null) =>
        new BackfillActionCodeText(ActionStore(), BackfillReader(), issues, TimeProvider.System).ExecuteAsync(BackfillOwner, limit, after);

    /// <summary>Settles a worker revision with retired text and the same facts and issues as the code path, as published records held before
    /// this change. Returns the code headline that the backfill writes.</summary>
    private async Task<string> SettleRetiredTextAsync(string candidate, RetiredText kind, Func<ActionDraftRevision, ActionDraftRevision, ActionDraftRevision>? change = null)
    {
        var actions = ActionStore();
        var evidence = (await actions.ReadEvidenceAsync(candidate, false, default))!;
        var snapshot = (await BackfillReader().ReadAsync(evidence, default)).Snapshot!;
        var issues = BillGateFixtures.Revision();
        var choice = BillIssueChoice.For(snapshot.Bill.PolicyArea, snapshot.Subjects, BillGate.DayOf(snapshot.Bill.UpdateDate), issues.ReadConfiguration());
        var context = new ActionDraftContext(evidence, snapshot, choice, issues.ReadConfiguration().PolicyAreaIdFor(snapshot.Bill.PolicyArea), issues.Id);
        var code = ActionDraftResolver.WriteByCode(context, UpdatedAt, ["rent"]);
        var retired = kind == RetiredText.IntroducedByHeadline
            ? code with { Headline = "H.R. 7: introduced by Elizabeth Warren" }
            : code with
            {
                AuthorSubject = ReviewAuthor.AnalysisSubject,
                TextOrigin = ReviewTextOrigin.Model,
                BillCitation = null,
                Headline = "The bill funds rent aid.",
                Summary = "The bill funds rent aid for tenants."
            };
        retired = change?.Invoke(retired, code) ?? retired;
        var claim = (await actions.TryClaimAsync(candidate, Lease, default))!;
        var run = kind == RetiredText.IntroducedByHeadline ? CodeRun(candidate) : DraftedRun(await ReserveAsync(actions, claim), candidate);
        Assert.True(await actions.CheckpointAsync(ActionDraftCheckpoint.From(claim, retired, evidence.ReviewStateVersion), run, default));
        return code.Headline;
    }

    private Task PublishActionAsync(string lastRelease = "NULL") => ExecuteAsync(connectionString, $"""
        SET session_replication_role = replica;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('action', '{RecordId}', 1, 1, {lastRelease});
        SET session_replication_role = origin;
        """);
}
