using CivicLens.Application.Paging;
using CivicLens.Application.Reports;
using CivicLens.Application.Review;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Reports.Persistence;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Reports;

public sealed class PostgresReaderReportStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IReaderReportStore
{
    private const string InsertReports = """
        INSERT INTO reader_reports (report_id, record_id, kind, revision_number, category, body, received_at_utc_ticks, status, version)
        SELECT r.report_id, r.record_id, r.kind, r.revision_number, r.category, r.body, r.received_at_utc_ticks, 'Open', 0
        FROM unnest({0}::uuid[], {1}::text[], {2}::text[], {3}::int[], {4}::text[], {5}::text[], {6}::bigint[])
             AS r(report_id, record_id, kind, revision_number, category, body, received_at_utc_ticks)
        ON CONFLICT (report_id) DO NOTHING
        """;

    private const string SaveCursor = """
        INSERT INTO reader_report_cursor (id, cursor) VALUES ({0}, {1})
        ON CONFLICT (id) DO UPDATE SET cursor = EXCLUDED.cursor
        """;

    private const string CurrentRevisions = """
        SELECT kind AS "Kind", id AS "RecordId", current_revision_number AS "RevisionNumber" FROM (
            SELECT 'DocumentChange' AS kind, draft_id AS id, current_revision_number FROM document_change_drafts
            UNION ALL SELECT 'Statement', candidate_id, current_revision_number FROM statement_drafts
            UNION ALL SELECT 'Action', record_id, current_revision_number FROM action_drafts
            UNION ALL SELECT 'Vote', record_id, current_revision_number FROM vote_drafts
            UNION ALL SELECT 'Brief', record_id, current_revision_number FROM brief_drafts
            UNION ALL SELECT 'Relationship', record_id, current_revision_number FROM relationship_drafts
            UNION ALL SELECT 'ExecutiveAction', record_id, current_revision_number FROM executive_action_drafts) drafts
        WHERE id = ANY({0}) AND current_revision_number >= 1
        """;

    public async Task<string?> GetCursorAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<ReaderReportCursorRow>().AsNoTracking().Select(row => row.Cursor).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task SavePulledAsync(PulledReportBatch batch, CancellationToken cancellationToken)
    {
        var reports = batch.Reports;
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (reports.Length > 0)
            await db.Database.ExecuteSqlRawAsync(InsertReports, [
                reports.Select(report => report.ReportId).ToArray(), reports.Select(report => report.RecordId).ToArray(),
                reports.Select(report => report.Kind.ToString()).ToArray(), reports.Select(report => report.RevisionNumber).ToArray(),
                reports.Select(report => report.Category).ToArray(), reports.Select(report => report.Text).ToArray(),
                reports.Select(report => report.ReceivedAtUtc.UtcTicks).ToArray()], cancellationToken);
        await db.Database.ExecuteSqlRawAsync(SaveCursor, [ReaderReportCursorRow.OnlyId, batch.NextCursor], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<ReaderReportPage> ListAsync(PageCursor? cursor, int limit, ReaderReportFilter filter,
        CancellationToken cancellationToken)
    {
        PageLimit.Validate(limit);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var open = filter == ReaderReportFilter.Open;
        var rows = db.Set<ReaderReportRow>().AsNoTracking();
        rows = open ? rows.Where(row => row.Status == ReaderReportStatus.Open) : rows.Where(row => row.Status != ReaderReportStatus.Open);
        var fetched = await ReviewKeyset.Page(rows, cursor, row => row.ReceivedAtUtcTicks, row => row.ReportId.ToString(), ascending: open)
            .Take(limit + 1).ToArrayAsync(cancellationToken);
        var slice = KeysetSlice<ReaderReportRow>.Create(fetched, limit, cursor, row => (row.ReceivedAtUtcTicks, row.ReportId.ToString()));
        var revisions = await ReadCurrentRevisionsAsync(db, slice.Items, cancellationToken);
        return new([.. slice.Items.Select(row => new ReaderReportListItem(ToReport(row), CurrentOf(revisions, row)))],
            slice.NewerCursor, slice.OlderCursor);
    }

    public async Task<int> CountOpenAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Set<ReaderReportRow>().CountAsync(row => row.Status == ReaderReportStatus.Open, cancellationToken);
    }

    public async Task<ReaderReportListItem?> GetAsync(Guid reportId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.Set<ReaderReportRow>().AsNoTracking().SingleOrDefaultAsync(report => report.ReportId == reportId, cancellationToken);
        if (row is null) return null;
        var revisions = await ReadCurrentRevisionsAsync(db, [row], cancellationToken);
        return new(ToReport(row), CurrentOf(revisions, row));
    }

    public async Task ResolveAsync(string actorSubject, Guid reportId, int expectedVersion, ReaderReportStatus status, string? note,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = await DatabaseClock.ReadAsync(db, cancellationToken);
        var resolved = await db.Set<ReaderReportRow>()
            .Where(row => row.ReportId == reportId && row.Status == ReaderReportStatus.Open && row.Version == expectedVersion)
            .ExecuteUpdateAsync(set => set
                .SetProperty(row => row.Status, status)
                .SetProperty(row => row.Note, note)
                .SetProperty(row => row.ResolvedBy, actorSubject)
                .SetProperty(row => row.ResolvedAtUtcTicks, now.Ticks)
                .SetProperty(row => row.Version, row => row.Version + 1), cancellationToken);
        if (resolved == 1) return;
        if (!await db.Set<ReaderReportRow>().AnyAsync(row => row.ReportId == reportId, cancellationToken))
            throw new ArgumentException("The report does not exist.", nameof(reportId));
        throw new ReviewConflictException("This report is already resolved.");
    }

    public async Task<int> DeleteExpiredClosedAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var now = await DatabaseClock.ReadAsync(db, cancellationToken);
        var cutoffTicks = now.Ticks - TimeSpan.FromDays(ReaderReportProtocol.ClosedRetentionDays).Ticks;
        return await db.Set<ReaderReportRow>()
            .Where(row => row.Status != ReaderReportStatus.Open && row.ResolvedAtUtcTicks < cutoffTicks)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReaderReport>> ListUnansweredCorrectionsAsync(PublishedRecordKind kind, string recordId,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Set<ReaderReportRow>().AsNoTracking()
            .Where(row => row.RecordId == recordId && row.Kind == kind && row.Status == ReaderReportStatus.CorrectionNeeded)
            .OrderBy(row => row.ReceivedAtUtcTicks).ThenBy(row => row.ReportId).ToArrayAsync(cancellationToken);
        if (rows.Length == 0) return [];
        var published = await ReviewQueries.ReadActiveRevisionNumberAsync(db, kind, recordId, cancellationToken);
        return [.. rows.Where(row => published is null || published <= row.RevisionNumber).Select(ToReport)];
    }

    private static async Task<Dictionary<(PublishedRecordKind Kind, string RecordId), int>> ReadCurrentRevisionsAsync(
        CollectionAttemptDbContext db, IEnumerable<ReaderReportRow> rows, CancellationToken cancellationToken)
    {
        var recordIds = rows.Select(row => row.RecordId).Distinct().ToArray();
        var current = await db.Database.SqlQueryRaw<CurrentRevisionRow>(CurrentRevisions, (object)recordIds).ToArrayAsync(cancellationToken);
        return current.ToDictionary(item => (Enum.Parse<PublishedRecordKind>(item.Kind), item.RecordId), item => item.RevisionNumber);
    }

    private static int? CurrentOf(Dictionary<(PublishedRecordKind Kind, string RecordId), int> revisions, ReaderReportRow row) =>
        revisions.TryGetValue((row.Kind, row.RecordId), out var revision) ? revision : null;

    private static ReaderReport ToReport(ReaderReportRow row) =>
        new(row.ReportId, row.RecordId, row.Kind, row.RevisionNumber, row.Category, row.Text, new DateTimeOffset(row.ReceivedAtUtcTicks, TimeSpan.Zero),
            row.Status, row.Note, row.ResolvedBy, row.ResolvedAtUtcTicks is { } ticks ? new DateTimeOffset(ticks, TimeSpan.Zero) : null, row.Version);

    private sealed class CurrentRevisionRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }
}
