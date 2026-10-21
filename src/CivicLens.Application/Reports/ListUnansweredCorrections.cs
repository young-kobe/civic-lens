using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Reports;

public sealed class ListUnansweredCorrections(IReaderReportStore store)
{
    public Task<IReadOnlyList<ReaderReport>> ExecuteAsync(ReviewActor actor, PublishedRecordKind kind, string recordId,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.ListUnansweredCorrectionsAsync(kind, recordId, cancellationToken);
    }
}
