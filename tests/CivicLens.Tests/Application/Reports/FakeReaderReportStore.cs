using CivicLens.Application.Paging;
using CivicLens.Application.Reports;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Reports;

internal sealed class FakeReaderReportStore : IReaderReportStore
{
    public string? Cursor { get; private set; }
    public List<Guid> Stored { get; } = [];
    public List<(string Actor, Guid ReportId, int ExpectedVersion, ReaderReportStatus Status, string? Note)> Resolutions { get; } = [];

    public Task<string?> GetCursorAsync(CancellationToken cancellationToken) => Task.FromResult(Cursor);

    public Task SavePulledAsync(PulledReportBatch batch, CancellationToken cancellationToken)
    {
        Stored.AddRange(batch.Reports.Select(report => report.ReportId));
        Cursor = batch.NextCursor;
        return Task.CompletedTask;
    }

    public Task ResolveAsync(string actorSubject, Guid reportId, int expectedVersion, ReaderReportStatus status, string? note,
        CancellationToken cancellationToken)
    {
        Resolutions.Add((actorSubject, reportId, expectedVersion, status, note));
        return Task.CompletedTask;
    }

    public Exception? DeleteFailure { get; set; }
    public int DeleteCalls { get; private set; }

    public Task<int> DeleteExpiredClosedAsync(CancellationToken cancellationToken)
    {
        DeleteCalls++;
        return DeleteFailure is { } failure ? Task.FromException<int>(failure) : Task.FromResult(0);
    }

    public Task<ReaderReportPage> ListAsync(PageCursor? cursor, int limit, ReaderReportFilter filter, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<int> CountOpenAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<ReaderReportListItem?> GetAsync(Guid reportId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task<IReadOnlyList<ReaderReport>> ListUnansweredCorrectionsAsync(PublishedRecordKind kind, string recordId,
        CancellationToken cancellationToken) => throw new NotSupportedException();
}
