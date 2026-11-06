using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

internal static class PublishedEntries
{
    private const string InsertOfficials = """
        INSERT INTO published_entry_officials (kind, record_id, revision_number, position, official_id, is_matched)
        SELECT k.kind, k.record_id, k.revision_number, k.position, k.official_id, k.is_matched
        FROM unnest({0}::text[], {1}::text[], {2}::int[], {3}::smallint[], {4}::text[], {5}::boolean[])
            AS k(kind, record_id, revision_number, position, official_id, is_matched)
        """;

    private const string ReadEntries = """
        SELECT e.* FROM published_entries e
        JOIN unnest({0}::text[], {1}::text[], {2}::int[]) AS k(kind, record_id, revision_number)
            ON e.kind = k.kind AND e.record_id = k.record_id AND e.revision_number = k.revision_number
        """;

    private const string ReadReleaseStates = """
        SELECT s.kind AS "Kind", s.record_id AS "RecordId", s.revision_number AS "RevisionNumber"
        FROM publication_record_states s
        WHERE s.first_release <= {0} AND (s.last_release IS NULL OR s.last_release >= {0})
        """;

    private const string FillBillLines = """
        WITH locked AS (
            SELECT e.kind, e.record_id, e.revision_number, k.bill_line
            FROM published_entries e
            JOIN unnest({0}::text[], {1}::text[], {2}::int[], {3}::text[]) AS k(kind, record_id, revision_number, bill_line)
                ON e.kind = k.kind AND e.record_id = k.record_id AND e.revision_number = k.revision_number
            WHERE e.bill_line IS NULL
            ORDER BY e.kind, e.record_id, e.revision_number
            FOR UPDATE OF e)
        UPDATE published_entries e SET bill_line = locked.bill_line
        FROM locked
        WHERE e.kind = locked.kind AND e.record_id = locked.record_id AND e.revision_number = locked.revision_number
        """;

    private const string ReadDaysWithoutCongress = """
        SELECT e.kind AS "Kind", e.record_id AS "RecordId", e.revision_number AS "RevisionNumber", e.event_date AS "EventDate",
            p.first_published_at_utc_ticks AS "FirstPublishedAtUtcTicks"
        FROM published_entries e
        JOIN published_records p ON p.kind = e.kind AND p.record_id = e.record_id
        WHERE e.congress IS NULL AND (e.kind, e.record_id, e.revision_number) > ({0}, {1}, {2})
        ORDER BY e.kind, e.record_id, e.revision_number
        LIMIT {3}
        """;

    private const string FillCongresses = """
        UPDATE published_entries e SET congress = k.congress
        FROM unnest({0}::text[], {1}::text[], {2}::int[], {3}::int[]) AS k(kind, record_id, revision_number, congress)
        WHERE e.kind = k.kind AND e.record_id = k.record_id AND e.revision_number = k.revision_number AND e.congress IS NULL
        """;

    private static readonly string InsertEntries = BuildInsertEntries();

    private const string ReadCandidates = """
        SELECT s.kind AS "Kind", s.record_id AS "RecordId", s.revision_number AS "RevisionNumber", s.first_release AS "FirstRelease",
            s.last_release AS "LastRelease", EXISTS (
                SELECT 1 FROM published_entries e
                WHERE e.kind = s.kind AND e.record_id = s.record_id AND e.revision_number = s.revision_number) AS "HasEntry", EXISTS (
                SELECT 1 FROM published_entries e
                WHERE e.kind = s.kind AND e.record_id = s.record_id AND e.revision_number = s.revision_number
                    AND e.kind = 'action' AND e.bill_line IS NULL) AS "LacksBillLine"
        FROM publication_record_states s
        ORDER BY s.kind, s.record_id, s.revision_number
        """;

    public static async Task<IReadOnlyList<PublishedEntryCandidate>> ReadCandidatesAsync(CollectionAttemptDbContext db,
        CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<CandidateRow>(ReadCandidates).ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => new PublishedEntryCandidate(KindOf(row.Kind), row.RecordId,
            row.RevisionNumber, row.FirstRelease, row.LastRelease, row.HasEntry, row.LacksBillLine))];
    }

    public static async Task<IReadOnlyList<StoredReleaseRecord>> ReadReleaseAsync(CollectionAttemptDbContext db, int releaseNumber,
        CancellationToken cancellationToken)
    {
        var states = await db.Database.SqlQueryRaw<ReleaseStateRow>(ReadReleaseStates, releaseNumber).ToArrayAsync(cancellationToken);
        var facts = await ReadFactsAsync(db, KeyArrays(states.Select(state => (state.Kind, state.RecordId, state.RevisionNumber))), cancellationToken);
        var references = states.Select(state => new PublishedRecordReference(KindOf(state.Kind), state.RecordId)).Distinct().ToArray();
        var firstPublished = await PublishedRecords.ReadAsync(db, references, cancellationToken);
        return [.. states.Select(state => new StoredReleaseRecord(KindOf(state.Kind), state.RecordId, state.RevisionNumber,
            firstPublished.TryGetValue(new(KindOf(state.Kind), state.RecordId), out var time) ? time : null,
            facts.GetValueOrDefault((state.Kind, state.RecordId, state.RevisionNumber))))];
    }

    public static async Task<int> InsertAsync(CollectionAttemptDbContext db, IReadOnlyCollection<PublishedRecordEntry> entries,
        string conflictAdvice, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return 0;
        await RequireLinkedRecordsAsync(db, entries, cancellationToken);
        var rows = await db.Database.SqlQueryRaw<EntryKeyRow>(InsertEntries, ScalarColumnsOf(entries)).ToArrayAsync(cancellationToken);
        var created = rows.Select(row => (row.Kind, row.RecordId, row.RevisionNumber)).ToHashSet();
        var createdEntries = entries.Where(entry => created.Contains(KeyOf(entry))).ToList();
        await InsertChildrenAsync(db, createdEntries, cancellationToken);
        await FillBillLinesAsync(db, [.. entries.Except(createdEntries)], conflictAdvice, cancellationToken);
        return createdEntries.Count;
    }

    /// <summary>
    /// Writes the bill line of action rows that hold none. It and <see cref="FillCongressesAsync"/> are the only updates of an entry row. A row that holds a line stays as it is, and
    /// <see cref="RequireSameAsync"/> then refuses a line that differs. The insert calls it for the rows that already exist.
    /// </summary>
    public static async Task<int> FillBillLinesAsync(CollectionAttemptDbContext db, IReadOnlyCollection<PublishedRecordEntry> entries,
        string conflictAdvice, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return 0;
        var withLine = entries.Where(entry => entry.BillLine is not null).ToArray();
        var filled = withLine.Length == 0 ? 0 : await db.Database.ExecuteSqlRawAsync(FillBillLines,
            [.. KeyArrays(withLine.Select(KeyOf)), withLine.Select(entry => entry.BillLine).ToArray()], cancellationToken);
        await RequireSameAsync(db, [.. entries], conflictAdvice, cancellationToken);
        return filled;
    }

    public static async Task<IReadOnlyList<PublishedEntryDay>> ReadDaysWithoutCongressAsync(CollectionAttemptDbContext db, PublishedEntryDay? after, int limit,
        CancellationToken cancellationToken)
    {
        var rows = await db.Database.SqlQueryRaw<EntryDayRow>(ReadDaysWithoutCongress, after?.Kind ?? "", after?.RecordId ?? "", after?.RevisionNumber ?? 0, limit)
            .ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => new PublishedEntryDay(row.Kind, row.RecordId, row.RevisionNumber, row.EventDate,
            new DateTimeOffset(row.FirstPublishedAtUtcTicks, TimeSpan.Zero)))];
    }

    public static Task<int> FillCongressesAsync(CollectionAttemptDbContext db, IReadOnlyCollection<PublishedEntryDay> entries, CancellationToken cancellationToken)
    {
        var dated = entries.Where(entry => entry.Congress is not null).ToArray();
        return dated.Length == 0 ? Task.FromResult(0) : db.Database.ExecuteSqlRawAsync(FillCongresses, [dated.Select(entry => entry.Kind).ToArray(),
            dated.Select(entry => entry.RecordId).ToArray(), dated.Select(entry => entry.RevisionNumber).ToArray(),
            dated.Select(entry => entry.Congress!.Value).ToArray()], cancellationToken);
    }

    private static string BuildInsertEntries()
    {
        var revisionColumns = Enum.GetValues<PublishedRecordKind>().Select(PublishedEntryRevisionColumns.For).ToArray();
        var kinds = Enum.GetValues<PublishedRecordKind>().Select(PublishedRecordReference.NameOf).ToArray();
        var columns = revisionColumns.SelectMany(pair => new[] { pair.RecordId, pair.RevisionNumber });
        var values = kinds.SelectMany(kind => new[] { $"CASE WHEN k.kind = '{kind}' THEN k.record_id END", $"CASE WHEN k.kind = '{kind}' THEN k.revision_number END" });
        return $$"""
            INSERT INTO published_entries (kind, record_id, revision_number, {{string.Join(", ", columns)}}, headline, source_host, event_date, bill_title,
                link_type, statement_record_id, target_kind, target_record_id, document_type, bill_line, congress)
            SELECT k.kind, k.record_id, k.revision_number, {{string.Join(", ", values)}}, k.headline, k.source_host, k.event_date, k.bill_title,
                k.link_type, k.statement_record_id, k.target_kind, k.target_record_id, k.document_type, k.bill_line, k.congress
            FROM unnest({0}::text[], {1}::text[], {2}::int[], {3}::text[], {4}::text[], {5}::date[], {6}::text[], {7}::text[], {8}::text[],
                {9}::text[], {10}::text[], {11}::text[], {12}::text[], {13}::int[])
                AS k(kind, record_id, revision_number, headline, source_host, event_date, bill_title, link_type, statement_record_id, target_kind,
                    target_record_id, document_type, bill_line, congress)
            ON CONFLICT DO NOTHING
            RETURNING kind AS "Kind", record_id AS "RecordId", revision_number AS "RevisionNumber"
            """;
    }

    private static async Task RequireLinkedRecordsAsync(CollectionAttemptDbContext db, IReadOnlyCollection<PublishedRecordEntry> entries,
        CancellationToken cancellationToken)
    {
        var linked = entries.SelectMany(PublishedRecordReference.LinkedBy).Distinct().ToArray();
        var stored = await PublishedRecords.ReadAsync(db, linked, cancellationToken);
        var unrecorded = linked.FirstOrDefault(reference => !stored.ContainsKey(reference));
        if (unrecorded is null) return;
        throw new FirstPublishedMissingException(
            $"The first publication time of {unrecorded} is not stored. Run releases first-published backfill, then publish again.");
    }

    private static async Task InsertChildrenAsync(CollectionAttemptDbContext db, List<PublishedRecordEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;
        var officials = Flatten(entries, entry => entry.Officials);
        await db.Database.ExecuteSqlRawAsync(InsertOfficials, [.. KeysOf(officials), officials.Select(row => row.Value.Id).ToArray(),
            officials.Select(row => row.Value.Id == row.Entry.MatchedOfficialId).ToArray()], cancellationToken);
        await InsertValuesAsync(db, "published_entry_issues", "issue_id", Flatten(entries, entry => entry.IssueIds), cancellationToken);
        await InsertValuesAsync(db, "published_entry_policy_areas", "policy_area_id", Flatten(entries, entry => entry.PolicyAreaIds), cancellationToken);
    }

    private static Task InsertValuesAsync(CollectionAttemptDbContext db, string table, string column, List<ChildRow<string>> rows,
        CancellationToken cancellationToken)
    {
        var sql = $$"""
            INSERT INTO {{table}} (kind, record_id, revision_number, position, {{column}})
            SELECT k.kind, k.record_id, k.revision_number, k.position, k.value
            FROM unnest({0}::text[], {1}::text[], {2}::int[], {3}::smallint[], {4}::text[]) AS k(kind, record_id, revision_number, position, value)
            """;
        return db.Database.ExecuteSqlRawAsync(sql, [.. KeysOf(rows), rows.Select(row => row.Value).ToArray()], cancellationToken);
    }

    private static async Task RequireSameAsync(CollectionAttemptDbContext db, List<PublishedRecordEntry> entries, string conflictAdvice,
        CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;
        var stored = await ReadFactsAsync(db, KeyArrays(entries.Select(KeyOf)), cancellationToken);
        foreach (var entry in entries)
        {
            if (!stored.TryGetValue(KeyOf(entry), out var existing) || Serialize(existing) != Serialize(FactsOf(entry)))
                throw new PublicationConflictException(
                    $"The stored entry of {entry.RecordId} revision {entry.RevisionNumber} differs from the release entry. {conflictAdvice}");
        }
    }

    internal static PublishedRecordKind KindOf(string name) => Enum.Parse<PublishedRecordKind>(name, ignoreCase: true);

    private static string Serialize(PublishedEntryFacts facts) => JsonSerializer.Serialize(facts);

    private static PublishedEntryFacts FactsOf(PublishedRecordEntry entry) => new(entry.Headline, entry.SourceHost, entry.EventDate, entry.BillTitle, entry.LinkType,
        entry.StatementRecordId, entry.TargetKind, entry.TargetRecordId, entry.DocumentType,
        [.. entry.Officials.Select(official => official.Id)], entry.MatchedOfficialId, entry.IssueIds, entry.PolicyAreaIds, entry.BillLine);

    private static object[] KeyArrays(IEnumerable<(string Kind, string RecordId, int RevisionNumber)> keys)
    {
        var all = keys.ToArray();
        return [all.Select(key => key.Kind).ToArray(), all.Select(key => key.RecordId).ToArray(), all.Select(key => key.RevisionNumber).ToArray()];
    }

    private static async Task<Dictionary<(string Kind, string RecordId, int RevisionNumber), PublishedEntryFacts>> ReadFactsAsync(
        CollectionAttemptDbContext db, object[] keys, CancellationToken cancellationToken)
    {
        var rows = await db.Set<PublishedEntryRow>().FromSqlRaw(ReadEntries, keys).AsNoTracking().ToArrayAsync(cancellationToken);
        var officials = await ReadChildrenAsync<PublishedEntryOfficialRow>(db, "published_entry_officials", keys, cancellationToken);
        var issues = await ReadChildrenAsync<PublishedEntryIssueRow>(db, "published_entry_issues", keys, cancellationToken);
        var policyAreas = await ReadChildrenAsync<PublishedEntryPolicyAreaRow>(db, "published_entry_policy_areas", keys, cancellationToken);
        return rows.ToDictionary(row => (row.Kind, row.RecordId, row.RevisionNumber), row =>
        {
            var key = (row.Kind, row.RecordId, row.RevisionNumber);
            return new PublishedEntryFacts(row.Headline, row.SourceHost, row.EventDate, row.BillTitle, row.LinkType, row.StatementRecordId,
                row.TargetKind is null ? null : KindOf(row.TargetKind), row.TargetRecordId, row.DocumentType, [.. officials[key].Select(official => official.OfficialId)],
                officials[key].SingleOrDefault(official => official.IsMatched)?.OfficialId,
                [.. issues[key].Select(issue => issue.Value)], [.. policyAreas[key].Select(area => area.Value)], row.BillLine);
        });
    }

    private static async Task<ILookup<(string Kind, string RecordId, int RevisionNumber), TRow>> ReadChildrenAsync<TRow>(CollectionAttemptDbContext db,
        string table, object[] keys, CancellationToken cancellationToken) where TRow : PublishedEntryChildRow
    {
        var sql = $$"""
            SELECT c.* FROM {{table}} c
            JOIN unnest({0}::text[], {1}::text[], {2}::int[]) AS k(kind, record_id, revision_number)
                ON c.kind = k.kind AND c.record_id = k.record_id AND c.revision_number = k.revision_number
            """;
        var rows = await db.Set<TRow>().FromSqlRaw(sql, keys).AsNoTracking().OrderBy(row => row.Position).ToArrayAsync(cancellationToken);
        return rows.ToLookup(row => (row.Kind, row.RecordId, row.RevisionNumber));
    }

    private static object[] ScalarColumnsOf(IReadOnlyCollection<PublishedRecordEntry> entries) =>
    [
        entries.Select(entry => PublishedRecordReference.NameOf(entry.Kind)).ToArray(), entries.Select(entry => entry.RecordId).ToArray(),
        entries.Select(entry => entry.RevisionNumber).ToArray(), entries.Select(entry => entry.Headline).ToArray(),
        entries.Select(entry => entry.SourceHost).ToArray(), entries.Select(entry => entry.EventDate).ToArray(),
        entries.Select(entry => entry.BillTitle).ToArray(), entries.Select(entry => entry.LinkType?.ToString()).ToArray(),
        entries.Select(entry => entry.StatementRecordId).ToArray(),
        entries.Select(entry => entry.TargetKind is { } target ? PublishedRecordReference.NameOf(target) : null).ToArray(),
        entries.Select(entry => entry.TargetRecordId).ToArray(), entries.Select(entry => entry.DocumentType?.ToString()).ToArray(),
        entries.Select(entry => entry.BillLine).ToArray(), entries.Select(ReleaseRecordOrder.CongressOf).ToArray()
    ];

    private static (string Kind, string RecordId, int RevisionNumber) KeyOf(PublishedRecordEntry entry) =>
        (PublishedRecordReference.NameOf(entry.Kind), entry.RecordId, entry.RevisionNumber);

    private static List<ChildRow<T>> Flatten<T>(List<PublishedRecordEntry> entries, Func<PublishedRecordEntry, IEnumerable<T>> children) =>
        [.. entries.SelectMany(entry => children(entry).Select((value, position) => new ChildRow<T>(entry, (short)position, value)))];

    private static object[] KeysOf<T>(List<ChildRow<T>> rows) =>
    [
        rows.Select(row => PublishedRecordReference.NameOf(row.Entry.Kind)).ToArray(), rows.Select(row => row.Entry.RecordId).ToArray(),
        rows.Select(row => row.Entry.RevisionNumber).ToArray(), rows.Select(row => row.Position).ToArray()
    ];

    private readonly record struct ChildRow<T>(PublishedRecordEntry Entry, short Position, T Value);

    private sealed class ReleaseStateRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }

    private sealed class CandidateRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
        public int FirstRelease { get; set; }
        public int? LastRelease { get; set; }
        public bool HasEntry { get; set; }
        public bool LacksBillLine { get; set; }
    }

    private sealed class EntryDayRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
        public DateOnly? EventDate { get; set; }
        public long FirstPublishedAtUtcTicks { get; set; }
    }

    private sealed class EntryKeyRow
    {
        public string Kind { get; set; } = "";
        public string RecordId { get; set; } = "";
        public int RevisionNumber { get; set; }
    }
}
