using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Votes;

/// <summary>Writes and verifies the immutable party tally copy for each exact vote revision.</summary>
internal static class VotePartyTallyRows
{
    private const string ReadMarkers = """
        SELECT r.record_id AS "RecordId", r.revision_number AS "RevisionNumber", r.party_tally_count AS "PartyTallyCount"
        FROM vote_revisions r
        JOIN unnest({0}::text[], {1}::int[]) AS k(record_id, revision_number)
            ON r.record_id = k.record_id AND r.revision_number = k.revision_number
        """;

    private const string ReadRows = """
        SELECT k.record_id AS "RecordId", k.revision_number AS "RevisionNumber", tally.party AS "Party",
            tally.yea AS "Yea", tally.nay AS "Nay", tally.present AS "Present", tally.not_voting AS "NotVoting", tally.other AS "Other"
        FROM unnest({0}::text[], {1}::int[]) AS k(record_id, revision_number)
        CROSS JOIN LATERAL (
            SELECT t.party, t.yea, t.nay, t.present, t.not_voting, t.other
            FROM vote_revision_party_tallies t
            WHERE t.record_id = k.record_id AND t.revision_number = k.revision_number
            LIMIT {2}
        ) tally
        """;

    private const string InsertRows = """
        INSERT INTO vote_revision_party_tallies (record_id, revision_number, party, yea, nay, present, not_voting, other)
        SELECT k.record_id, k.revision_number, k.party, k.yea, k.nay, k.present, k.not_voting, k.other
        FROM unnest({0}::text[], {1}::int[], {2}::text[], {3}::smallint[], {4}::smallint[], {5}::smallint[], {6}::smallint[], {7}::smallint[])
            AS k(record_id, revision_number, party, yea, nay, present, not_voting, other)
        """;

    private const string SetMarkers = """
        UPDATE vote_revisions r SET party_tally_count = k.tally_count
        FROM unnest({0}::text[], {1}::int[], {2}::smallint[]) AS k(record_id, revision_number, tally_count)
        WHERE r.record_id = k.record_id AND r.revision_number = k.revision_number AND r.party_tally_count IS NULL
        """;

    public static async Task WriteAsync(CollectionAttemptDbContext db, IReadOnlyCollection<VoteStoredColumns> columns, CancellationToken cancellationToken)
    {
        if (columns.Select(column => (column.RecordId, column.RevisionNumber)).Distinct().Count() != columns.Count)
            throw new InvalidOperationException("A vote revision can appear only once in a party tally write.");
        var inputs = columns.Select(column => new TallyInput(column, Validate(column))).ToArray();
        foreach (var batch in inputs.Chunk(25)) await WriteBatchAsync(db, batch, cancellationToken);
    }

    private static async Task WriteBatchAsync(CollectionAttemptDbContext db, TallyInput[] batch, CancellationToken cancellationToken)
    {
        var records = batch.Select(input => input.Column.RecordId).ToArray();
        var revisions = batch.Select(input => input.Column.RevisionNumber).ToArray();
        var markers = await db.Database.SqlQueryRaw<TallyMarkerRow>(ReadMarkers, records, revisions).ToArrayAsync(cancellationToken);
        if (markers.Length != batch.Length) throw new InvalidOperationException("A vote revision for party tallies does not exist.");
        var markerByKey = markers.ToDictionary(row => (row.RecordId, row.RevisionNumber));
        var stored = await ReadRowsAsync(db, records, revisions, cancellationToken);
        var storedByKey = stored.GroupBy(row => (row.RecordId, row.RevisionNumber)).ToDictionary(group => group.Key, group => group.ToArray());

        foreach (var input in batch)
        {
            var key = (input.Column.RecordId, input.Column.RevisionNumber);
            var actual = storedByKey.GetValueOrDefault(key) ?? [];
            var marker = markerByKey[key].PartyTallyCount;
            if (marker is null)
            {
                if (actual.Length != 0) throw Conflict(input.Column);
                continue;
            }
            if (marker != input.Tallies.Length || actual.Length != marker || !Matches(actual, input.Tallies)) throw Conflict(input.Column);
        }

        var pending = batch.Where(input => markerByKey[(input.Column.RecordId, input.Column.RevisionNumber)].PartyTallyCount is null).ToArray();
        if (pending.Length == 0) return;

        var rows = pending.SelectMany(input => input.Tallies.Select(tally => new TallyWriteRow(input.Column.RecordId, input.Column.RevisionNumber, tally))).ToArray();
        if (rows.Length > 0)
            await db.Database.ExecuteSqlRawAsync(InsertRows, [rows.Select(row => row.RecordId).ToArray(), rows.Select(row => row.RevisionNumber).ToArray(), rows.Select(row => row.Tally.Party).ToArray(),
                rows.Select(row => (short)row.Tally.Yea).ToArray(), rows.Select(row => (short)row.Tally.Nay).ToArray(), rows.Select(row => (short)row.Tally.Present).ToArray(),
                rows.Select(row => (short)row.Tally.NotVoting).ToArray(), rows.Select(row => (short)row.Tally.Other).ToArray()], cancellationToken);

        var updated = await db.Database.ExecuteSqlRawAsync(SetMarkers, [pending.Select(input => input.Column.RecordId).ToArray(),
            pending.Select(input => input.Column.RevisionNumber).ToArray(), pending.Select(input => (short)input.Tallies.Length).ToArray()], cancellationToken);
        if (updated != pending.Length) throw new InvalidOperationException("Party tally markers changed while a vote revision was being written.");
        var writtenMarkers = await db.Database.SqlQueryRaw<TallyMarkerRow>(ReadMarkers, pending.Select(input => input.Column.RecordId).ToArray(),
            pending.Select(input => input.Column.RevisionNumber).ToArray()).ToArrayAsync(cancellationToken);
        if (writtenMarkers.Length != pending.Length) throw new InvalidOperationException("Party tally markers could not be read back.");
        var writtenMarkerByKey = writtenMarkers.ToDictionary(row => (row.RecordId, row.RevisionNumber));
        var writtenRows = await ReadRowsAsync(db, pending.Select(input => input.Column.RecordId).ToArray(), pending.Select(input => input.Column.RevisionNumber).ToArray(), cancellationToken);
        var writtenByKey = writtenRows.GroupBy(row => (row.RecordId, row.RevisionNumber)).ToDictionary(group => group.Key, group => group.ToArray());
        foreach (var input in pending)
        {
            var key = (input.Column.RecordId, input.Column.RevisionNumber);
            var actual = writtenByKey.GetValueOrDefault(key) ?? [];
            if (writtenMarkerByKey[key].PartyTallyCount != input.Tallies.Length || actual.Length != input.Tallies.Length || !Matches(actual, input.Tallies))
                throw Conflict(input.Column);
        }
    }

    private static Task<TallyReadRow[]> ReadRowsAsync(CollectionAttemptDbContext db, string[] records, int[] revisions, CancellationToken cancellationToken) =>
        db.Database.SqlQueryRaw<TallyReadRow>(ReadRows, records, revisions, PublishedVote.MaximumParties + 1).ToArrayAsync(cancellationToken);

    private static PublishedPartyTally[] Validate(VoteStoredColumns column)
    {
        if (column.PartyTallies.IsDefault) return [];
        if (column.PartyTallies.IsEmpty) throw new InvalidDataException("A nondefault published party tally array cannot be empty.");
        if (column.PartyTallies.Length > PublishedVote.MaximumParties)
            throw new InvalidDataException("Published vote has too many party tallies.");
        var tallies = column.PartyTallies.ToArray();
        if (tallies.Any(tally => tally is null)) throw new InvalidDataException("Published party tallies cannot contain null groups.");
        foreach (var tally in tallies) tally.Validate();
        var total = tallies.Sum(tally => (long)tally.Yea + tally.Nay + tally.Present + tally.NotVoting + tally.Other);
        if (tallies.Select(tally => tally.Party).Distinct(StringComparer.Ordinal).Count() != tallies.Length || total > PublishedVote.MaximumMembers)
            throw new InvalidDataException("Published vote party tallies are invalid.");
        return tallies;
    }

    private static bool Matches(TallyReadRow[] actual, PublishedPartyTally[] expected)
    {
        if (actual.Length != expected.Length) return false;
        var byParty = expected.ToDictionary(tally => tally.Party, StringComparer.Ordinal);
        return actual.All(row => byParty.TryGetValue(row.Party, out var tally) &&
            row.Yea == tally.Yea && row.Nay == tally.Nay && row.Present == tally.Present && row.NotVoting == tally.NotVoting && row.Other == tally.Other);
    }

    private static InvalidOperationException Conflict(VoteStoredColumns column) =>
        new($"Stored party tallies conflict with vote revision {column.RecordId}/{column.RevisionNumber}.");

    private sealed record TallyInput(VoteStoredColumns Column, PublishedPartyTally[] Tallies);
    private sealed record TallyWriteRow(string RecordId, int RevisionNumber, PublishedPartyTally Tally);
    private sealed class TallyMarkerRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
        public short? PartyTallyCount { get; set; }
    }

    private sealed class TallyReadRow
    {
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
        public string Party { get; set; } = "";
        public int Yea { get; set; }
        public int Nay { get; set; }
        public int Present { get; set; }
        public int NotVoting { get; set; }
        public int Other { get; set; }
    }
}
