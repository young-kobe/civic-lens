using System.Collections.Immutable;
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
    public static void Add(CollectionAttemptDbContext db, int releaseNumber, ImmutableArray<PublishedBillStatus> statuses) =>
        db.AddRange(statuses.Select(status => RowOf(releaseNumber, status)));

    /// <summary>Writes the rows that a kept release lacks. A row that exists stays as it is. Returns the number of rows that this call wrote.</summary>
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

    private static ReleaseBillStatusRow RowOf(int releaseNumber, PublishedBillStatus status) => new()
    {
        ReleaseNumber = releaseNumber,
        RecordId = status.RecordId,
        LatestActionText = status.Text,
        LatestActionDate = status.Date,
        ReadAtUtcTicks = status.ObservedAtUtc.UtcTicks
    };
}
