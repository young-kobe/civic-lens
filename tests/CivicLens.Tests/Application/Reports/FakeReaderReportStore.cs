using CivicLens.Application.Paging;
using CivicLens.Application.Reports;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Reports;

internal sealed class FakeReaderReportStore : IReaderReportStore
{
    public string? Cursor { get; private set; }
    public List<Guid> Stored { get; } = [];
    public List<(string Actor, Guid ReportId, int ExpectedVersion)> Dismissals { get; } = [];
    public List<(string Actor, PublishedRecordKind Kind, string RecordId, Guid[] ReportIds, string Note)> Closures { get; } = [];

    public Task<string?> GetCursorAsync(CancellationToken cancellationToken) => Task.FromResult(Cursor);

    public Task SavePulledAsync(PulledReportBatch batch, CancellationToken cancellationToken)
    {
        Stored.AddRange(batch.Reports.Select(report => report.ReportId));
        Cursor = batch.NextCursor;
        return Task.CompletedTask;
    }

    public Task DismissAsync(string actorSubject, Guid reportId, int expectedVersion, CancellationToken cancellationToken)
    {
        Dismissals.Add((actorSubject, reportId, expectedVersion));
        return Task.CompletedTask;
    }

    public Task CloseCorrectedAsync(string actorSubject, PublishedRecordKind kind, string recordId, IReadOnlyCollection<Guid> reportIds,
        string note, CancellationToken cancellationToken)
    {
        Closures.Add((actorSubject, kind, recordId, [.. reportIds], note));
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

    public Task<IReadOnlyList<ReaderReport>> ListForRecordAsync(PublishedRecordKind kind, string recordId, CancellationToken cancellationToken) =>
        throw new NotSupportedException();
}
