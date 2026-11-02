using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Reports;

public sealed class CloseCorrectedReports(IReaderReportStore store)
{
    public const string Note = "Approved with a correction.";

    public Task ExecuteAsync(ReviewActor actor, PublishedRecordKind kind, string recordId, IReadOnlyCollection<Guid> reportIds,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        return reportIds.Count == 0
            ? Task.CompletedTask
            : store.CloseCorrectedAsync(actor.Subject, kind, recordId, reportIds, Note, cancellationToken);
    }
}
