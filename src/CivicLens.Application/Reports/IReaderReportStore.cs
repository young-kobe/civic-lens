using CivicLens.Application.Paging;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Reports;

public interface IReaderReportStore
{
    Task<string?> GetCursorAsync(CancellationToken cancellationToken);

    /// <summary>Stores the reports that are new and the cursor in one transaction. A report that is already stored stays as it is.</summary>
    Task SavePulledAsync(PulledReportBatch batch, CancellationToken cancellationToken);

    Task<ReaderReportPage> ListAsync(PageCursor? cursor, int limit, ReaderReportFilter filter, CancellationToken cancellationToken);

    Task<int> CountOpenAsync(CancellationToken cancellationToken);

    Task<ReaderReportListItem?> GetAsync(Guid reportId, CancellationToken cancellationToken);

    /// <summary>Dismisses an open report at the expected version. A report that is resolved or changed throws <see cref="ReviewConflictException"/>.</summary>
    Task DismissAsync(string actorSubject, Guid reportId, int expectedVersion, CancellationToken cancellationToken);

    /// <summary>Deletes the closed reports that were resolved more than <see cref="ReaderReportProtocol.ClosedRetentionDays"/> days ago by the database clock. An open report stays. Returns the number deleted.</summary>
    Task<int> DeleteExpiredClosedAsync(CancellationToken cancellationToken);

    /// <summary>Lists the oldest <see cref="ListRecordReports.MaximumShown"/> open reports of the record, and the reports closed with a correction until the published revision passes the revision of the report.</summary>
    Task<RecordReports> ListForRecordAsync(PublishedRecordKind kind, string recordId, CancellationToken cancellationToken);
}
