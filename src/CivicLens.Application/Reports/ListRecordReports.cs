using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Reports;

public sealed class ListRecordReports(IReaderReportStore store)
{
    public const int MaximumShown = 64;

    public Task<RecordReports> ExecuteAsync(ReviewActor actor, PublishedRecordKind kind, string recordId,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.ListForRecordAsync(kind, recordId, cancellationToken);
    }
}
