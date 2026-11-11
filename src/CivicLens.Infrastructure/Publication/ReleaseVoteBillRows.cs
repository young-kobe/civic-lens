using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

/// <summary>
/// Stores the bill action that each vote of one release links. The commit writes the rows once, with the release number, so a later release never changes them.
/// The view public_api.votes reads the row by key and never searches the bill view.
/// </summary>
internal static class ReleaseVoteBillRows
{
    public static void Add(CollectionAttemptDbContext db, int releaseNumber, ImmutableArray<ReleaseVoteBill> voteBills) =>
        db.AddRange(voteBills.Select(voteBill => new ReleaseVoteBillRow
        {
            ReleaseNumber = releaseNumber,
            VoteRecordId = voteBill.VoteRecordId,
            BillRecordId = voteBill.BillActionRecordId
        }));

    public static async Task<IReadOnlyList<ReleaseVoteBill>> ReadAsync(CollectionAttemptDbContext db, int releaseNumber, CancellationToken cancellationToken) =>
        await db.Set<ReleaseVoteBillRow>().AsNoTracking().Where(row => row.ReleaseNumber == releaseNumber)
            .OrderBy(row => row.VoteRecordId).Select(row => new ReleaseVoteBill(row.VoteRecordId, row.BillRecordId)).ToListAsync(cancellationToken);

    /// <summary>Writes the rows that a kept release lacks. A row that exists stays as it is. Returns the number of rows that this call wrote.</summary>
    public static async Task<int> InsertMissingAsync(CollectionAttemptDbContext db, int releaseNumber, IReadOnlyCollection<ReleaseVoteBill> voteBills,
        CancellationToken cancellationToken)
    {
        if (voteBills.Count == 0) return 0;
        return await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO release_vote_bills (release_number, vote_record_id, bill_record_id)
            SELECT {0}, k.vote_record_id, k.bill_record_id
            FROM unnest({1}::text[], {2}::text[]) AS k(vote_record_id, bill_record_id)
            ON CONFLICT DO NOTHING
            """, [releaseNumber, voteBills.Select(voteBill => voteBill.VoteRecordId).ToArray(), voteBills.Select(voteBill => voteBill.BillActionRecordId).ToArray()],
            cancellationToken);
    }
}
