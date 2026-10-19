using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class ListStatementTriage(IStatementReviewStore store, StatementSelectionResolver selections)
{
    public async Task<ReviewTriagePage<StatementReviewListItem>> ExecuteAsync(ReviewActor actor, string? cursor,
        DraftListFilter filter = default, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ReviewValidation.ValidateListFilter(filter);
        var read = await store.ListTriageAsync(ReviewValidation.ValidatePage(cursor, ReviewTriage.PageSize), filter, cancellationToken);
        return new([.. read.Items.Select(RowOf)], read.NewerCursor, read.OlderCursor);
    }

    private ReviewTriageRow<StatementReviewListItem> RowOf(StatementTriageEvidence evidence)
    {
        var item = evidence.Item;
        var revision = item.CurrentRevision!;
        var check = ApprovalCheck.Of(() => selections.Resolve(evidence.ConfigurationRevision, item.SourceId,
            StatementArticle.CapturedOn(evidence.Extraction)).Allowed.RequireApprovable(revision, evidence.Extraction));
        var extractions = ImmutableDictionary.CreateRange([KeyValuePair.Create(evidence.Extraction.ExtractionId, evidence.Extraction)]);
        return new(item, check, check.Passed
            ? [.. revision.Quotations.Select(quotation => ReviewTriageQuote.Of("Quotation", quotation.Span, extractions))] : []);
    }
}
