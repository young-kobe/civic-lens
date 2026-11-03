using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Reports;

public sealed class DismissReaderReport(IReaderReportStore store)
{
    public Task ExecuteAsync(ReviewActor actor, Guid reportId, int expectedVersion, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return store.DismissAsync(actor.Subject, reportId, expectedVersion, cancellationToken);
    }
}
