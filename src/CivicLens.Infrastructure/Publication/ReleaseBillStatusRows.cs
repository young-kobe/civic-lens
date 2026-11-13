using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

/// <summary>
/// Stores the bill statuses that one release pins. The commit writes them once, with the release number, so a later release never changes them.
/// The view public_api.bill_status reads these rows and never the live bill gates.
/// </summary>
internal static class ReleaseBillStatusRows
{
    public static void Add(CollectionAttemptDbContext db, int releaseNumber, ImmutableArray<ActionBillStatus> statuses) =>
        db.AddRange(statuses.Select(status => RowOf(releaseNumber, status)));

    /// <summary>
    /// Writes the rows that a kept release lacks, with no stage, because the status file holds no bill type. A row that exists stays as it is.
    /// <see cref="FillStagesAsync"/> fills the stage later. Returns the number of rows that this call wrote.
    /// </summary>
    public static async Task<int> InsertMissingAsync(CollectionAttemptDbContext db, int releaseNumber, IReadOnlyCollection<PublishedBillStatus> statuses,
        CancellationToken cancellationToken)
    {
        if (statuses.Count == 0) return 0;
        return await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO release_bill_statuses (release_number, record_id, latest_action_text, latest_action_date, read_at_utc_ticks)
            SELECT {0}, k.record_id, k.text, k.action_date, k.read_at_utc_ticks
            FROM unnest({1}::text[], {2}::text[], {3}::date[], {4}::bigint[]) AS k(record_id, text, action_date, read_at_utc_ticks)
            ON CONFLICT DO NOTHING
            """, [releaseNumber, statuses.Select(status => status.RecordId).ToArray(), statuses.Select(status => status.Text).ToArray(),
                statuses.Select(status => status.Date).ToArray(), statuses.Select(status => status.ObservedAtUtc.UtcTicks).ToArray()], cancellationToken);
    }

    /// <summary>Reads up to <paramref name="limit"/> rows with no stage that follow <paramref name="after"/> in key order. The bill type comes from the revisions of the record.</summary>
    public static async Task<IReadOnlyList<BillStatusGap>> ReadGapsAsync(CollectionAttemptDbContext db, BillStatusGap? after, int limit, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<BillStatusGapRow>("""
            SELECT s.release_number AS "ReleaseNumber", s.record_id AS "RecordId", s.latest_action_text AS "Text", t.bill_type AS "BillType"
            FROM release_bill_statuses s
            CROSS JOIN LATERAL (SELECT r.bill_type FROM action_revisions r WHERE r.record_id = s.record_id ORDER BY r.revision_number DESC LIMIT 1) t
            WHERE s.status IS NULL AND (s.release_number, s.record_id) > ({0}, {1})
            ORDER BY s.release_number, s.record_id
            LIMIT {2}
            """, after?.ReleaseNumber ?? 0, after?.RecordId ?? "", limit).ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => new BillStatusGap(row.ReleaseNumber, row.RecordId, row.Text, row.BillType))];
    }

    /// <summary>Writes the stage of rows that hold none. A row that holds a stage stays as it is. Returns the number of rows that this call wrote.</summary>
    public static Task<int> FillStagesAsync(CollectionAttemptDbContext db, IReadOnlyCollection<BillStatusGap> gaps, CancellationToken cancellationToken) =>
        gaps.Count == 0 ? Task.FromResult(0) : db.Database.ExecuteSqlRawAsync("""
            UPDATE release_bill_statuses s SET status = k.status
            FROM unnest({0}::int[], {1}::text[], {2}::text[]) AS k(release_number, record_id, status)
            WHERE s.release_number = k.release_number AND s.record_id = k.record_id AND s.status IS NULL
            """, [gaps.Select(gap => gap.ReleaseNumber).ToArray(), gaps.Select(gap => gap.RecordId).ToArray(),
                gaps.Select(gap => gap.Stage.ToString()).ToArray()], cancellationToken);

    private sealed class BillStatusGapRow
    {
        public int ReleaseNumber { get; set; }
        public string RecordId { get; set; } = "";
        public string Text { get; set; } = "";
        public string BillType { get; set; } = "";
    }

    private static ReleaseBillStatusRow RowOf(int releaseNumber, ActionBillStatus status) => new()
    {
        ReleaseNumber = releaseNumber,
        RecordId = status.Status.RecordId,
        LatestActionText = status.Status.Text,
        LatestActionDate = status.Status.Date,
        ReadAtUtcTicks = status.Status.ObservedAtUtc.UtcTicks,
        Status = BillStatusMeaning.StageOf(status.Status.Text, status.BillType)
    };
}
