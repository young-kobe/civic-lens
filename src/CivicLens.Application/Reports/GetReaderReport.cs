using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Reports;

public sealed class GetReaderReport(IReaderReportStore store)
{
    public Task<ReaderReportListItem?> ExecuteAsync(ReviewActor actor, Guid reportId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.GetAsync(reportId, cancellationToken);
    }
}
