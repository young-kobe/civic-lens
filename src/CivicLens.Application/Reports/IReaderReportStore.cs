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

    /// <summary>Resolves an open report at the expected version. A report that is resolved or changed throws <see cref="ReviewConflictException"/>.</summary>
    Task ResolveAsync(string actorSubject, Guid reportId, int expectedVersion, ReaderReportStatus status, string? note,
        CancellationToken cancellationToken);

    /// <summary>Lists the reports that need a correction on the record, until the published revision passes the revision of the report.</summary>
    Task<IReadOnlyList<ReaderReport>> ListUnansweredCorrectionsAsync(PublishedRecordKind kind, string recordId,
        CancellationToken cancellationToken);
}
