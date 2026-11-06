using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

/// <summary>
/// Writes the vote values that <see cref="VoteStoredColumns"/> holds. A shared member list keeps one party for each member. The first write fills it,
/// and a later revision of the same list must name the same parties, or the write refuses it. The revision columns follow from the list parties and casts,
/// so the same parties give the same columns.
/// </summary>
internal static class VoteStoredColumnRows
{
    private const string ReadParties = """
        SELECT r.record_id AS "RecordId", r.revision_number AS "RevisionNumber", e.position AS "Position", e.party AS "Party"
        FROM vote_revisions r
        JOIN unnest({0}::text[], {1}::int[]) AS k(record_id, revision_number) ON r.record_id = k.record_id AND r.revision_number = k.revision_number
        JOIN vote_member_list_entries e ON e.list_id = r.member_list_id
        """;

    private const string FillParties = """
        UPDATE vote_member_list_entries e SET party = k.party
        FROM vote_revisions r,
            unnest({0}::text[], {1}::int[], {2}::smallint[], {3}::text[]) AS k(record_id, revision_number, position, party)
        WHERE r.record_id = k.record_id AND r.revision_number = k.revision_number AND e.list_id = r.member_list_id AND e.position = k.position AND e.party IS NULL
        """;

    private const string FillRevisions = """
        UPDATE vote_revisions r SET alignment = k.alignment, democrat_majority = k.democrat_majority, republican_majority = k.republican_majority
        FROM unnest({0}::text[], {1}::int[], {2}::text[], {3}::text[], {4}::text[]) AS k(record_id, revision_number, alignment, democrat_majority, republican_majority)
        WHERE r.record_id = k.record_id AND r.revision_number = k.revision_number
        """;

    private const string ReadGaps = """
        SELECT r.record_id AS "RecordId", r.revision_number AS "RevisionNumber"
        FROM vote_revisions r
        WHERE r.member_list_id IS NOT NULL
            AND EXISTS (SELECT 1 FROM publication_record_states s WHERE s.kind = 'vote' AND s.record_id = r.record_id AND s.revision_number = r.revision_number)
            AND ((r.alignment IS NULL AND r.democrat_majority IS NULL AND r.republican_majority IS NULL)
                OR EXISTS (SELECT 1 FROM vote_member_list_entries e WHERE e.list_id = r.member_list_id AND e.party IS NULL))
        ORDER BY r.record_id, r.revision_number
        """;

    public static async Task<IReadOnlyList<PublishedRevisionBinding>> ReadGapsAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken) =>
        [.. (await db.Database.SqlQueryRaw<KeyRow>(ReadGaps).ToArrayAsync(cancellationToken))
            .Select(row => new PublishedRevisionBinding(PublishedRecordKind.Vote, row.RecordId, row.RevisionNumber))];

    public static async Task<IReadOnlyList<PublishedRevisionBinding>> WriteAsync(CollectionAttemptDbContext db, IReadOnlyCollection<VoteStoredColumns> columns,
        CancellationToken cancellationToken)
    {
        if (columns.Count == 0) return [];
        var stored = (await db.Database.SqlQueryRaw<PartyRow>(ReadParties, columns.Select(column => column.RecordId).ToArray(),
            columns.Select(column => column.RevisionNumber).ToArray()).ToArrayAsync(cancellationToken)).ToLookup(row => (row.RecordId, row.RevisionNumber));
        var listed = columns.Where(column => stored[(column.RecordId, column.RevisionNumber)].Any()).ToArray();
        var conflicts = listed.Where(column => HoldsOtherParties(column, stored[(column.RecordId, column.RevisionNumber)])).ToArray();
        var writable = listed.Except(conflicts).ToArray();
        await FillPartiesAsync(db, writable, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(FillRevisions, [writable.Select(column => column.RecordId).ToArray(), writable.Select(column => column.RevisionNumber).ToArray(),
            writable.Select(column => column.Alignment?.ToString()).ToArray(), writable.Select(column => column.DemocratMajority).ToArray(),
            writable.Select(column => column.RepublicanMajority).ToArray()], cancellationToken);
        return [.. conflicts.Select(column => new PublishedRevisionBinding(PublishedRecordKind.Vote, column.RecordId, column.RevisionNumber))];
    }

    private static bool HoldsOtherParties(VoteStoredColumns column, IEnumerable<PartyRow> stored)
    {
        var rows = stored.ToArray();
        return rows.Length != column.MemberParties.Length ||
            rows.Any(row => row.Party is not null && row.Party != column.MemberParties[row.Position]);
    }

    private static Task FillPartiesAsync(CollectionAttemptDbContext db, VoteStoredColumns[] writable, CancellationToken cancellationToken)
    {
        var rows = writable.SelectMany(column => column.MemberParties.Select((party, position) => (column.RecordId, column.RevisionNumber, Position: (short)position, Party: party))).ToArray();
        return db.Database.ExecuteSqlRawAsync(FillParties, [rows.Select(row => row.RecordId).ToArray(), rows.Select(row => row.RevisionNumber).ToArray(),
            rows.Select(row => row.Position).ToArray(), rows.Select(row => row.Party).ToArray()], cancellationToken);
    }

    private sealed class KeyRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }

    private sealed class PartyRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
        public short Position { get; set; }
        public string? Party { get; set; }
    }
}
