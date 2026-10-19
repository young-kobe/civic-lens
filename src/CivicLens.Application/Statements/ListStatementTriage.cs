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
        return new([.. read.Items.Select(RowOf)],
            read.Items.Select(evidence => evidence.Extraction).DistinctBy(extraction => extraction.ExtractionId)
                .ToImmutableDictionary(extraction => extraction.ExtractionId, StringComparer.Ordinal),
            read.NewerCursor, read.OlderCursor);
    }

    private ReviewTriageRow<StatementReviewListItem> RowOf(StatementTriageEvidence evidence)
    {
        var item = evidence.Item;
        var allowed = selections.Resolve(evidence.ConfigurationRevision, item.SourceId, StatementArticle.CapturedOn(evidence.Extraction)).Allowed;
        return new(item, ApprovalCheck.Of(() => allowed.RequireApprovable(item.CurrentRevision!, evidence.Extraction)));
    }
}
