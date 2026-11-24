using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

internal static class VoteTotalRows
{
    private const string Read = """
        SELECT r.record_id AS "RecordId", r.revision_number AS "RevisionNumber", r.totals_written AS "Written",
            r.total_yea AS "Yea", r.total_nay AS "Nay", r.total_present AS "Present", r.total_not_voting AS "NotVoting"
        FROM vote_revisions r JOIN unnest({0}::text[], {1}::int[]) AS k(record_id, revision_number)
            ON r.record_id = k.record_id AND r.revision_number = k.revision_number
        """;

    private const string Write = """
        UPDATE vote_revisions r SET total_yea = k.yea, total_nay = k.nay, total_present = k.present,
            total_not_voting = k.not_voting, totals_written = true
        FROM unnest({0}::text[], {1}::int[], {2}::int[], {3}::int[], {4}::int[], {5}::int[])
            AS k(record_id, revision_number, yea, nay, present, not_voting)
        WHERE r.record_id = k.record_id AND r.revision_number = k.revision_number AND NOT r.totals_written
        """;

    public static async Task WriteAsync(CollectionAttemptDbContext db, IReadOnlyCollection<VoteStoredColumns> columns, CancellationToken cancellationToken)
    {
        if (columns.Select(column => (column.RecordId, column.RevisionNumber)).Distinct().Count() != columns.Count)
            throw new InvalidOperationException("A vote revision can appear only once in a whole totals write.");
        foreach (var column in columns)
        {
            if (!column.TotalsKnown && column.Totals is not null)
                throw new InvalidDataException("Unknown vote totals cannot contain counts.");
            column.Totals?.Validate();
        }
        foreach (var batch in columns.Where(column => column.TotalsKnown).Chunk(25))
            await WriteBatchAsync(db, batch, cancellationToken);
    }

    private static async Task WriteBatchAsync(CollectionAttemptDbContext db, VoteStoredColumns[] batch, CancellationToken cancellationToken)
    {
        var rows = await ReadAsync(db, batch, cancellationToken);
        var byKey = rows.ToDictionary(row => (row.RecordId, row.RevisionNumber));
        foreach (var column in batch)
        {
            var actual = byKey[(column.RecordId, column.RevisionNumber)];
            if (actual.Written ? !Matches(actual, column) : actual.Yea is not null || actual.Nay is not null || actual.Present is not null || actual.NotVoting is not null)
                throw Conflict(column);
        }
        var pending = batch.Where(column => !byKey[(column.RecordId, column.RevisionNumber)].Written).ToArray();
        if (pending.Length == 0) return;
        var updated = await db.Database.ExecuteSqlRawAsync(Write, [pending.Select(column => column.RecordId).ToArray(),
            pending.Select(column => column.RevisionNumber).ToArray(), pending.Select(column => column.Totals?.Yea).ToArray(),
            pending.Select(column => column.Totals?.Nay).ToArray(), pending.Select(column => column.Totals?.Present).ToArray(),
            pending.Select(column => column.Totals?.NotVoting).ToArray()], cancellationToken);
        if (updated != pending.Length) throw new InvalidOperationException("Whole totals markers changed while a vote revision was being written.");
        // Read back the immutable copy before its surrounding publication or backfill transaction commits.
        var written = await ReadAsync(db, pending, cancellationToken);
        var expected = pending.ToDictionary(column => (column.RecordId, column.RevisionNumber));
        foreach (var row in written)
        {
            var column = expected[(row.RecordId, row.RevisionNumber)];
            if (!row.Written || !Matches(row, column)) throw Conflict(column);
        }
    }

    private static async Task<TotalRow[]> ReadAsync(CollectionAttemptDbContext db, VoteStoredColumns[] columns, CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<TotalRow>(Read, columns.Select(column => column.RecordId).ToArray(),
            columns.Select(column => column.RevisionNumber).ToArray()).ToArrayAsync(cancellationToken);
        if (rows.Length != columns.Length) throw new InvalidOperationException("A vote revision for whole totals does not exist.");
        return rows;
    }

    private static bool Matches(TotalRow row, VoteStoredColumns column) =>
        row.Yea == column.Totals?.Yea && row.Nay == column.Totals?.Nay && row.Present == column.Totals?.Present && row.NotVoting == column.Totals?.NotVoting;

    private static InvalidOperationException Conflict(VoteStoredColumns column) =>
        new($"Stored whole totals conflict with vote revision {column.RecordId}/{column.RevisionNumber}.");

    private sealed class TotalRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
        public bool Written { get; set; }
        public int? Yea { get; set; }
        public int? Nay { get; set; }
        public int? Present { get; set; }
        public int? NotVoting { get; set; }
    }
}
