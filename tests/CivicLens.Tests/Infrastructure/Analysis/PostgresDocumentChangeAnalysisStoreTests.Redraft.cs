using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Core.Analysis;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Analysis;

public sealed partial class PostgresDocumentChangeAnalysisStoreTests
{
    private const string OldPrompt = "document-change-draft-prompt-v0";

    [Fact]
    public async Task AStaleUntouchedModelDraftGetsANewModelRevisionLinkedToItsOwnRun()
    {
        var (comparison, draftId) = await DraftWithOldRunAsync("prompt_version", OldPrompt);

        Assert.Equal(1, await analysis.RequeueStaleAsync(default));
        Assert.Equal(AnalysisState.Pending, (await EntryAsync(comparison)).Status);
        var model = new FakeDocumentChangeDraftModel().Draft(FakeDocumentChangeDraftModel.Output("Deadline moves again", [("h1", "after", "October 30")]));
        await Worker(model).ExecuteAsync(once: true);

        var review = (await reviews.GetAsync(draftId, default))!;
        Assert.Equal((2, ReviewAuthor.AnalysisSubject, "Deadline moves again"),
            (review.CurrentRevision.RevisionNumber, review.CurrentRevision.AuthorSubject, review.CurrentRevision.Headline));
        Assert.Equal(AnalysisState.Succeeded, (await EntryAsync(comparison)).Status);
        var run = (await analysis.GetDraftRunAsync(draftId, default))!;
        Assert.Equal((DocumentChangeDraftTask.PromptVersion, AnalysisRunOutcome.Drafted), (run.PromptVersion, run.Outcome));
        Assert.Equal(2L, await ScalarAsync<long>("SELECT max(revision_number)::bigint FROM document_change_analysis_runs WHERE draft_id = '" + draftId + "'"));
        Assert.Equal(0, await analysis.RequeueStaleAsync(default));
    }

    [Theory]
    [InlineData("prompt_version", OldPrompt, 1)]
    [InlineData("schema_version", "document-change-draft-schema-v0", 1)]
    [InlineData("task_version", "document-change-draft-v0", 1)]
    [InlineData("prompt_version", DocumentChangeDraftTask.PromptVersion, 0)]
    public async Task OnlyADraftWhoseRunUsesAnOldVersionIsRequeued(string column, string value, int requeued)
    {
        var (comparison, _) = await DraftWithOldRunAsync(column, value);

        Assert.Equal(requeued, await analysis.RequeueStaleAsync(default));

        Assert.Equal(requeued == 1 ? AnalysisState.Pending : AnalysisState.Succeeded, (await EntryAsync(comparison)).Status);
    }

    [Fact]
    public async Task APersonRevisionBlocksTheRedraftEvenWhenTheWorkerRunsTheEntryAgain()
    {
        var (comparison, draftId) = await DraftWithOldRunAsync("prompt_version", OldPrompt);
        await SaveAsPersonAsync(draftId, "Edited headline");

        Assert.Equal(0, await analysis.RequeueStaleAsync(default));
        await ExecuteAsync(connectionString, "UPDATE document_change_analysis SET status = 'Pending', error_code = NULL");
        var model = new FakeDocumentChangeDraftModel();
        await Worker(model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal((AnalysisState.Succeeded, DocumentChangeDraftErrorCodes.HumanDraftExists), ((await EntryAsync(comparison)).Status, (await EntryAsync(comparison)).ErrorCode));
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM document_change_revisions"));
        Assert.Equal(2, (await reviews.GetAsync(draftId, default))!.CurrentRevision.RevisionNumber);
    }

    [Theory]
    [InlineData(ReviewDecisionKind.Approve)]
    [InlineData(ReviewDecisionKind.RequestChanges)]
    [InlineData(ReviewDecisionKind.Reject)]
    public async Task ADocumentChangeThatAPersonDecidedOnIsNeverRedrafted(ReviewDecisionKind kind)
    {
        var (comparison, draftId) = await DraftWithOldRunAsync("prompt_version", OldPrompt);
        await DecideAsync(draftId, kind);

        Assert.Equal(0, await analysis.RequeueStaleAsync(default));
        await ExecuteAsync(connectionString, "UPDATE document_change_analysis SET status = 'Pending', error_code = NULL");
        var model = new FakeDocumentChangeDraftModel();
        await Worker(model).ExecuteAsync(once: true);

        Assert.Empty(model.Requests);
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM document_change_revisions"));
        Assert.Equal(AnalysisState.Succeeded, (await EntryAsync(comparison)).Status);
    }

    [Fact]
    public async Task APersonSaveDuringTheModelCallFailsTheExpectedRevisionCheckAndAppendsNothing()
    {
        var (comparison, draftId) = await DraftWithOldRunAsync("prompt_version", OldPrompt);
        Assert.Equal(1, await analysis.RequeueStaleAsync(default));
        var model = new FakeDocumentChangeDraftModel().Reply(async (_, _) =>
        {
            await SaveAsPersonAsync(draftId, "Edited while the model ran");
            return new("end_turn", FakeDocumentChangeDraftModel.Output([("h1", "after", "October 30")]), FakeDocumentChangeDraftModel.Usage(900, 300), "req_fixture");
        });

        await Worker(model).ExecuteAsync(once: true);

        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.RetryWaiting, DocumentChangeDraftErrorCodes.RevisionConflict), (entry.Status, entry.ErrorCode));
        var current = (await reviews.GetAsync(draftId, default))!.CurrentRevision;
        Assert.Equal((2, Owner.Subject), (current.RevisionNumber, current.AuthorSubject));
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM document_change_revisions"));
        Assert.Equal("DraftDiscarded", (await RunOutcomesAsync(comparison))[^1]);

        await ExecuteAsync(connectionString, "UPDATE document_change_analysis SET status = 'Pending', error_code = NULL");
        var retryModel = new FakeDocumentChangeDraftModel();
        await Worker(retryModel).ExecuteAsync(once: true);

        Assert.Empty(retryModel.Requests);
        Assert.Equal((AnalysisState.Succeeded, DocumentChangeDraftErrorCodes.HumanDraftExists), ((await EntryAsync(comparison)).Status, (await EntryAsync(comparison)).ErrorCode));
        Assert.Equal(2L, await ScalarAsync<long>("SELECT count(*) FROM document_change_revisions"));
    }

    [Fact]
    public async Task ADecisionDuringTheModelCallDiscardsTheRedraft()
    {
        var (comparison, draftId) = await DraftWithOldRunAsync("prompt_version", OldPrompt);
        Assert.Equal(1, await analysis.RequeueStaleAsync(default));
        var model = new FakeDocumentChangeDraftModel().Reply(async (_, _) =>
        {
            await DecideAsync(draftId, ReviewDecisionKind.Approve);
            return new("end_turn", FakeDocumentChangeDraftModel.Output([("h1", "after", "October 30")]), FakeDocumentChangeDraftModel.Usage(900, 300), "req_fixture");
        });

        await Worker(model).ExecuteAsync(once: true);

        var entry = await EntryAsync(comparison);
        Assert.Equal((AnalysisState.Succeeded, DocumentChangeDraftErrorCodes.DraftDiscarded), (entry.Status, entry.ErrorCode));
        Assert.Equal(1L, await ScalarAsync<long>("SELECT count(*) FROM document_change_revisions"));
        Assert.Equal("DraftDiscarded", (await RunOutcomesAsync(comparison))[^1]);
    }

    private async Task<(CivicLens.Core.Documents.DocumentComparison Comparison, string DraftId)> DraftWithOldRunAsync(string column, string value)
    {
        var comparison = await seeder.SaveAsync("Deadline: October 15.\n", "Deadline: October 30.\n");
        await Worker(new FakeDocumentChangeDraftModel().Draft(FakeDocumentChangeDraftModel.Output(("h1", "after", "October 30")))).ExecuteAsync(once: true);
        var draftId = (await EntryAsync(comparison)).DraftId!;
        await ExecuteAsync(connectionString, $"UPDATE document_change_analysis_runs SET {column} = '{value}'");
        return (comparison, draftId);
    }

    private async Task SaveAsPersonAsync(string draftId, string headline)
    {
        var current = (await reviews.GetAsync(draftId, default))!.CurrentRevision;
        await new SaveDocumentChangeDraft(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new SaveDocumentChangeDraftRequest(draftId, current.RevisionNumber, headline, current.Summary, null, current.Limits,
                current.Institution, null, null, [], [], current.Citations, "save-" + Guid.NewGuid().ToString("N")));
    }

    private async Task DecideAsync(string draftId, ReviewDecisionKind kind) =>
        await new DecideDocumentChangeReview(reviews, new ReviewCatalog([], [])).ExecuteAsync(Owner,
            new DecideDocumentChangeReviewRequest(draftId, 1, 0, kind, kind == ReviewDecisionKind.RequestChanges ? "Fix the headline." : null, [],
                "decide-" + Guid.NewGuid().ToString("N"), null, kind == ReviewDecisionKind.Reject ? ReviewReasonCode.LowSubstance : null));
}
