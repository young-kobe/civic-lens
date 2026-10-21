using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Reports;

public sealed class CountOpenReaderReports(IReaderReportStore store)
{
    public Task<int> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.CountOpenAsync(cancellationToken);
    }
}
