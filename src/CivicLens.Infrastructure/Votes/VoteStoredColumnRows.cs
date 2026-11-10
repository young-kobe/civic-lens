using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

/// <summary>
/// Writes the vote values that <see cref="VoteStoredColumns"/> holds. A shared member list keeps one party for each member, and the first write pins them.
/// A later write never changes them, because the party of a member on the vote day is a fixed fact. The revision columns and the written marker go in one update,
/// and only for a revision that is not written yet.
/// </summary>
internal static class VoteStoredColumnRows
{
    private const string ReadLists = """
        SELECT r.record_id AS "RecordId", r.revision_number AS "RevisionNumber", r.member_list_id AS "ListId"
        FROM vote_revisions r
        JOIN unnest({0}::text[], {1}::int[]) AS k(record_id, revision_number) ON r.record_id = k.record_id AND r.revision_number = k.revision_number
        """;

    private const string FillParties = """
        UPDATE vote_member_list_entries e SET party = k.party
        FROM vote_revisions r,
            unnest({0}::text[], {1}::int[], {2}::smallint[], {3}::text[]) AS k(record_id, revision_number, position, party)
        WHERE r.record_id = k.record_id AND r.revision_number = k.revision_number AND e.list_id = r.member_list_id AND e.position = k.position AND e.party IS NULL
        """;

    private const string FillRevisions = """
        UPDATE vote_revisions r SET alignment = k.alignment, democrat_majority = k.democrat_majority, republican_majority = k.republican_majority, columns_written = true
        FROM unnest({0}::text[], {1}::int[], {2}::text[], {3}::text[], {4}::text[]) AS k(record_id, revision_number, alignment, democrat_majority, republican_majority)
        WHERE r.record_id = k.record_id AND r.revision_number = k.revision_number AND NOT r.columns_written
        """;

    // A revision counts as published when any record state row names it. The release range of the revision comes from all its state rows.
    private const string ReadGaps = """
        SELECT r.record_id AS "RecordId", r.revision_number AS "RevisionNumber", r.member_list_id AS "ListId",
            min(s.first_release) AS "FirstRelease", CASE WHEN bool_or(s.last_release IS NULL) THEN NULL ELSE max(s.last_release) END AS "LastRelease"
        FROM vote_revisions r
        JOIN publication_record_states s ON s.kind = 'vote' AND s.record_id = r.record_id AND s.revision_number = r.revision_number
        WHERE r.member_list_id IS NOT NULL AND (NOT r.columns_written OR r.party_tally_count IS NULL)
        GROUP BY r.record_id, r.revision_number, r.member_list_id
        ORDER BY r.member_list_id, r.record_id, r.revision_number
        """;

    public static async Task<IReadOnlyList<VoteColumnGap>> ReadGapsAsync(CollectionAttemptDbContext db, CancellationToken cancellationToken) =>
        [.. (await db.Database.SqlQueryRaw<GapRow>(ReadGaps).ToArrayAsync(cancellationToken))
            .Select(row => new VoteColumnGap(row.RecordId, row.RevisionNumber, row.ListId, row.FirstRelease, row.LastRelease))];

    public static async Task WriteAsync(CollectionAttemptDbContext db, IReadOnlyCollection<VoteStoredColumns> columns, CancellationToken cancellationToken)
    {
        if (columns.Count == 0) return;
        var pins = columns.Where(column => !column.MemberParties.IsEmpty).ToArray();
        await RequireOnePartySetForEachListAsync(db, pins, cancellationToken);
        await FillPartiesAsync(db, pins, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(FillRevisions, [columns.Select(column => column.RecordId).ToArray(), columns.Select(column => column.RevisionNumber).ToArray(),
            columns.Select(column => column.Alignment?.ToString()).ToArray(), columns.Select(column => column.DemocratMajority).ToArray(),
            columns.Select(column => column.RepublicanMajority).ToArray()], cancellationToken);
        await VotePartyTallyRows.WriteAsync(db, columns, cancellationToken);
    }

    // Two party sets for one list in one call would race inside one UPDATE. The caller chooses the one revision that owns the list.
    private static async Task RequireOnePartySetForEachListAsync(CollectionAttemptDbContext db, VoteStoredColumns[] pins, CancellationToken cancellationToken)
    {
        if (pins.Length < 2) return;
        var lists = (await db.Database.SqlQueryRaw<ListRow>(ReadLists, pins.Select(pin => pin.RecordId).ToArray(), pins.Select(pin => pin.RevisionNumber).ToArray())
            .ToArrayAsync(cancellationToken)).ToDictionary(row => (row.RecordId, row.RevisionNumber), row => row.ListId);
        var conflicting = pins.Where(pin => lists.ContainsKey((pin.RecordId, pin.RevisionNumber))).GroupBy(pin => lists[(pin.RecordId, pin.RevisionNumber)], StringComparer.Ordinal)
            .FirstOrDefault(group => group.Select(pin => string.Join('\n', pin.MemberParties)).Distinct(StringComparer.Ordinal).Count() > 1);
        if (conflicting is not null)
            throw new InvalidOperationException($"The write pins two different party sets on member list {conflicting.Key}.");
    }

    private static Task FillPartiesAsync(CollectionAttemptDbContext db, VoteStoredColumns[] pins, CancellationToken cancellationToken)
    {
        var rows = pins.SelectMany(column => column.MemberParties.Select((party, position) => (column.RecordId, column.RevisionNumber, Position: (short)position, Party: party))).ToArray();
        return db.Database.ExecuteSqlRawAsync(FillParties, [rows.Select(row => row.RecordId).ToArray(), rows.Select(row => row.RevisionNumber).ToArray(),
            rows.Select(row => row.Position).ToArray(), rows.Select(row => row.Party).ToArray()], cancellationToken);
    }

    private sealed class GapRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
        public string ListId { get; set; } = "";
        public int FirstRelease { get; set; }
        public int? LastRelease { get; set; }
    }

    private sealed class ListRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
        public string ListId { get; set; } = "";
    }
}
