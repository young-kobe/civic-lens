using CivicLens.Application.Analysis;
using CivicLens.Application.Paging;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Analysis;

public sealed class GetDocumentChangeReviewOutcomesTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    private static DocumentChangeDraftRevision Revision(string headline = "Headline") => new(new string('d', 32), 1,
        new string('c', 64), ReviewAuthor.AnalysisSubject, DateTimeOffset.UnixEpoch, headline, "Summary", null, null, "Institution",
        null, null, [], [], [new(new string('a', 64), 0, 5)]);

    private static AiDraftOutcome Outcome(string prompt, DocumentChangeReviewStatus status, string? editedHeadline = null)
    {
        var first = Revision();
        var current = editedHeadline is null ? first with { RevisionNumber = 2 } : Revision(editedHeadline) with { RevisionNumber = 2 };
        return new(prompt, first, new(first.DraftId, current, 0, status, 0));
    }

    [Fact]
    public async Task OutcomesAreSplitByPromptVersionSoOnePromptChangeCanBeJudgedAlone()
    {
        var store = new FakeReviewOutcomeStore([
            Outcome("v2", DocumentChangeReviewStatus.Approved),
            Outcome("v1", DocumentChangeReviewStatus.Approved, "Edited"),
            Outcome("v1", DocumentChangeReviewStatus.Approved)]);

        var result = await new GetDocumentChangeReviewOutcomes(store).ExecuteAsync(Owner);

        Assert.Equal(["v1", "v2"], result.Select(item => item.PromptVersion));
        Assert.Equal((2, 1, 1), (result[0].Drafts, result[0].ApprovedUnchanged, result[0].ApprovedEdited));
        Assert.Equal((1, 1, 0), (result[1].Drafts, result[1].ApprovedUnchanged, result[1].ApprovedEdited));
    }

    [Fact]
    public async Task OnlyApprovedDraftsCountFieldEditsAndEveryFieldIsReported()
    {
        var store = new FakeReviewOutcomeStore([
            Outcome("v1", DocumentChangeReviewStatus.Approved, "Edited"),
            Outcome("v1", DocumentChangeReviewStatus.ChangesRequested, "Edited")]);

        var result = Assert.Single(await new GetDocumentChangeReviewOutcomes(store).ExecuteAsync(Owner));

        Assert.Equal(1, result.FieldEdits[DocumentChangeField.Headline]);
        Assert.Equal(Enum.GetValues<DocumentChangeField>().Length, result.FieldEdits.Count);
        Assert.Equal(1, result.ChangesRequested);
    }

    [Fact]
    public async Task EveryNonApprovedStatusHasOneCounterAndConcernsFoldIntoAwaitingReview()
    {
        var store = new FakeReviewOutcomeStore([
            Outcome("v1", DocumentChangeReviewStatus.AwaitingReview),
            Outcome("v1", DocumentChangeReviewStatus.AwaitingReviewWithConcerns),
            Outcome("v1", DocumentChangeReviewStatus.ChangesRequested),
            Outcome("v1", DocumentChangeReviewStatus.ApprovalWithdrawn)]);

        var result = Assert.Single(await new GetDocumentChangeReviewOutcomes(store).ExecuteAsync(Owner));

        Assert.Equal((4, 2, 1, 1), (result.Drafts, result.AwaitingReview, result.ChangesRequested, result.ApprovalWithdrawn));
    }

    [Fact]
    public async Task EveryPageIsReadWithTheMaximumLimitSoTheReadStaysBoundedAndComplete()
    {
        var store = new FakeReviewOutcomeStore([.. Enumerable.Range(0, PageLimit.Maximum * 2 + 1)
            .Select(_ => Outcome("v1", DocumentChangeReviewStatus.AwaitingReview))]);

        var result = Assert.Single(await new GetDocumentChangeReviewOutcomes(store).ExecuteAsync(Owner));

        Assert.Equal(PageLimit.Maximum * 2 + 1, result.Drafts);
        Assert.Equal([PageLimit.Maximum, PageLimit.Maximum, PageLimit.Maximum], store.Limits);
    }

    [Fact]
    public async Task OnlyReviewersReadTheMetrics()
    {
        var handler = new GetDocumentChangeReviewOutcomes(new FakeReviewOutcomeStore([]));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            handler.ExecuteAsync(new ReviewActor(ReviewAuthor.AnalysisSubject, ReviewRole.Owner)));
    }
}
