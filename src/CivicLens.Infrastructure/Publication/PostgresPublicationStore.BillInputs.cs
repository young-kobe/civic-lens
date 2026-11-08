using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Infrastructure.Publication.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CivicLens.Infrastructure.Publication;

public sealed partial class PostgresPublicationStore
{
    public async Task<IReadOnlyList<BillPublicationInput>> ReadBillInputsAsync(int? previousReleaseNumber,
        ImmutableArray<string> recordIds, CancellationToken cancellationToken)
    {
        if (recordIds.IsDefaultOrEmpty) return [];
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Database.SqlQueryRaw<BillInputRow>($$"""
            SELECT a.record_id AS "RecordId", a.bill_type AS "BillType",
                   status.latest_action_text AS "Text", status.latest_action_date AS "Date",
                   status.created_at_utc_ticks AS "StatusObservedAtUtcTicks",
                   history.detail_job_id AS "DetailJobId", history.source_id AS "SourceId",
                   history.congress AS "Congress", history.bill_number AS "BillNumber",
                   history.observed_at_utc_ticks AS "HistoryObservedAtUtcTicks",
                   history.action_count AS "ActionCount",
                   prior.detail_job_id AS "PublishedHistoryDetailJobId",
                   prior.projection_version AS "PublishedHistoryProjectionVersion"
              FROM action_drafts a
              LEFT JOIN LATERAL (
                   SELECT g.latest_action_text, g.latest_action_date, g.created_at_utc_ticks
                     FROM bill_gates g
                    WHERE g.source_id = a.source_id AND g.congress = a.congress
                      AND g.bill_type = a.bill_type AND g.bill_number = a.bill_number
                    ORDER BY g.update_date_utc_ticks DESC, g.detail_job_id DESC LIMIT 1
              ) status ON true
              LEFT JOIN LATERAL (
                   SELECT g.detail_job_id, g.source_id, g.congress, g.bill_number, chain.observed_at_utc_ticks, chain.action_count
                     FROM bill_gates g
                     JOIN LATERAL (
                          {{BillHistoryCaptures.CompleteChainSql}}
                     ) chain ON true
                    WHERE g.source_id = a.source_id AND g.congress = a.congress
                      AND g.bill_type = a.bill_type AND g.bill_number = a.bill_number
                    ORDER BY g.update_date_utc_ticks DESC, g.detail_job_id DESC LIMIT 1
              ) history ON true
              LEFT JOIN release_bill_histories prior ON prior.release_number = {0} AND prior.record_id = a.record_id
             WHERE a.record_id = ANY({1})
             ORDER BY a.record_id
            """, previousReleaseNumber ?? 0, recordIds.ToArray()).ToArrayAsync(cancellationToken);
        return [.. rows.Select(ToInput)];
    }

    public async Task PrepareBillHistoriesAsync(IReadOnlyCollection<BillHistoryCapture> histories, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(histories);
        foreach (var batch in histories.Distinct().Chunk(256))
        {
            if (batch.Any(capture => capture is null || capture.ProjectionVersion != BillHistoryCapture.CurrentProjectionVersion))
                throw new ArgumentException("A history capture must use the current verified representation.", nameof(histories));
            await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
            var ids = batch.Select(capture => capture.DetailJobId).ToArray();
            var existing = await db.Set<PublishedBillHistoryRow>().AsNoTracking()
                .Where(row => row.ProjectionVersion == BillHistoryCapture.CurrentProjectionVersion && ids.Contains(row.DetailJobId))
                .ToDictionaryAsync(row => row.DetailJobId, StringComparer.Ordinal, cancellationToken);
            foreach (var capture in batch)
            {
                if (existing.TryGetValue(capture.DetailJobId, out var copy))
                {
                    PublishedBillHistories.RequireMatchingMetadata(copy, capture);
                    continue;
                }
                try
                {
                    await PublishedBillHistories.EnsureAsync(db, capture, cancellationToken);
                }
                catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.SerializationFailure)
                {
                    throw new PublicationConflictException("Another publication prepared this history. Retry the release.");
                }
            }
        }
    }

    private static BillPublicationInput ToInput(BillInputRow row) => new(row.RecordId,
        row.Text is null ? null : new ActionBillStatus(new PublishedBillStatus
        {
            RecordId = row.RecordId,
            Text = row.Text,
            Date = row.Date!.Value,
            ObservedAtUtc = new DateTimeOffset(row.StatusObservedAtUtcTicks!.Value, TimeSpan.Zero)
        }, row.BillType),
        row.DetailJobId is null ? null : new BillHistoryCapture(row.DetailJobId, row.SourceId!, row.Congress!.Value,
            row.BillType, row.BillNumber!.Value, new DateTimeOffset(row.HistoryObservedAtUtcTicks!.Value, TimeSpan.Zero),
            row.ActionCount!.Value, BillHistoryCapture.CurrentProjectionVersion),
        row.PublishedHistoryDetailJobId, row.PublishedHistoryProjectionVersion);

    private sealed record BillInputRow(string RecordId, string BillType, string? Text, DateOnly? Date, long? StatusObservedAtUtcTicks,
        string? DetailJobId, string? SourceId, int? Congress, int? BillNumber, long? HistoryObservedAtUtcTicks,
        int? ActionCount, string? PublishedHistoryDetailJobId, int? PublishedHistoryProjectionVersion);
}
