using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Reports;

public sealed class ListReaderReports(IReaderReportStore store)
{
    public Task<ReaderReportPage> ExecuteAsync(ReviewActor actor, string? cursor, int limit = PageLimit.Default,
        ReaderReportFilter filter = ReaderReportFilter.Open, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        if (!Enum.IsDefined(filter)) throw new ArgumentOutOfRangeException(nameof(filter));
        return store.ListAsync(ReviewValidation.ValidatePage(cursor, limit), limit, filter, cancellationToken);
    }
}
