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
        INSERT INTO published_entry_officials (kind, record_id, revision_number, position, official_id, official_name, is_matched)
        SELECT k.kind, k.record_id, k.revision_number, k.position, k.official_id, k.official_name, k.is_matched
        FROM unnest({0}::text[], {1}::text[], {2}::int[], {3}::smallint[], {4}::text[], {5}::text[], {6}::boolean[])
            AS k(kind, record_id, revision_number, position, official_id, official_name, is_matched)
        """;

    private const string ReadEntries = """
        SELECT e.* FROM published_entries e
        JOIN unnest({0}::text[], {1}::text[], {2}::int[]) AS k(kind, record_id, revision_number)
            ON e.kind = k.kind AND e.record_id = k.record_id AND e.revision_number = k.revision_number
        """;

    private static readonly Dictionary<PublishedRecordKind, string> InsertByKind = Enum.GetValues<PublishedRecordKind>().ToDictionary(kind => kind, kind =>
    {
        var (id, number) = PublishedEntryRevisionColumns.For(kind);
        return $"INSERT INTO published_entries (kind, record_id, revision_number, {id}, {number}, headline, source_host, event_date, bill_title, " +
            "link_type, statement_record_id, target_kind, target_record_id, document_type, yea_count, nay_count, alignment) " +
            "VALUES ({0}, {1}, {2}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11}, {12}, {13}, {14}) ON CONFLICT DO NOTHING";
    });

    public static async Task InsertAsync(CollectionAttemptDbContext db, IReadOnlyCollection<PublishedRecordEntry> entries,
        CancellationToken cancellationToken)
    {
        var created = new List<PublishedRecordEntry>();
        var existing = new List<PublishedRecordEntry>();
        foreach (var entry in entries)
        {
            var inserted = await db.Database.ExecuteSqlRawAsync(InsertByKind[entry.Kind], ScalarsOf(entry).Select(value => value!), cancellationToken);
            (inserted == 1 ? created : existing).Add(entry);
        }
        await InsertChildrenAsync(db, created, cancellationToken);
        await RequireSameAsync(db, existing, cancellationToken);
    }

    private static async Task InsertChildrenAsync(CollectionAttemptDbContext db, List<PublishedRecordEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;
        var officials = Flatten(entries, entry => entry.Officials);
        await db.Database.ExecuteSqlRawAsync(InsertOfficials, [.. KeysOf(officials), officials.Select(row => row.Value.Id).ToArray(),
            officials.Select(row => row.Value.Name).ToArray(),
            officials.Select(row => row.Value.Id == row.Entry.MatchedOfficialId).ToArray()], cancellationToken);
        await InsertValuesAsync(db, "published_entry_issues", "issue_id", Flatten(entries, entry => entry.IssueIds), cancellationToken);
        await InsertValuesAsync(db, "published_entry_policy_areas", "policy_area_id", Flatten(entries, entry => entry.PolicyAreaIds), cancellationToken);
        await InsertValuesAsync(db, "published_entry_cosponsors", "official_id", Flatten(entries, entry => entry.CosponsorIds ?? []), cancellationToken);
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

    private static async Task RequireSameAsync(CollectionAttemptDbContext db, List<PublishedRecordEntry> entries, CancellationToken cancellationToken)
    {
        if (entries.Count == 0) return;
        var stored = await ReadAsync(db, entries, cancellationToken);
        foreach (var entry in entries)
        {
            if (!stored.TryGetValue((entry.Kind, entry.RecordId, entry.RevisionNumber), out var existing) || Serialize(existing) != Serialize(entry))
                throw new PublicationConflictException(
                    $"The stored entry of {entry.RecordId} revision {entry.RevisionNumber} differs from the release entry. Rebuild the release.");
        }
    }

    private static string Serialize(PublishedRecordEntry entry) => JsonSerializer.Serialize(entry, PublicationProtocol.JsonOptions);

    private static async Task<Dictionary<(PublishedRecordKind Kind, string RecordId, int RevisionNumber), PublishedRecordEntry>> ReadAsync(
        CollectionAttemptDbContext db, List<PublishedRecordEntry> entries, CancellationToken cancellationToken)
    {
        object[] keys = [entries.Select(entry => PublishedRecordReference.NameOf(entry.Kind)).ToArray(),
            entries.Select(entry => entry.RecordId).ToArray(), entries.Select(entry => entry.RevisionNumber).ToArray()];
        var rows = await db.Set<PublishedEntryRow>().FromSqlRaw(ReadEntries, keys).AsNoTracking().ToArrayAsync(cancellationToken);
        var firstPublished = await PublishedRecords.ReadAsync(db, [.. entries.Select(entry => new PublishedRecordReference(entry.Kind, entry.RecordId))],
            cancellationToken);
        var officials = await ReadChildrenAsync<PublishedEntryOfficialRow>(db, "published_entry_officials", keys, cancellationToken);
        var issues = await ReadChildrenAsync<PublishedEntryIssueRow>(db, "published_entry_issues", keys, cancellationToken);
        var policyAreas = await ReadChildrenAsync<PublishedEntryPolicyAreaRow>(db, "published_entry_policy_areas", keys, cancellationToken);
        var cosponsors = await ReadChildrenAsync<PublishedEntryCosponsorRow>(db, "published_entry_cosponsors", keys, cancellationToken);
        return rows.ToDictionary(row => (Kind: KindOf(row.Kind), row.RecordId, row.RevisionNumber), row =>
        {
            var kind = KindOf(row.Kind);
            var key = (row.Kind, row.RecordId, row.RevisionNumber);
            return new PublishedRecordEntry
            {
                Kind = kind,
                RecordId = row.RecordId,
                RevisionNumber = row.RevisionNumber,
                Headline = row.Headline,
                FirstPublishedAtUtc = firstPublished[new(kind, row.RecordId)],
                Officials = [.. officials[key].Select(official => new PublishedOfficial { Id = official.OfficialId, Name = official.OfficialName })],
                IssueIds = [.. issues[key].Select(issue => issue.Value)],
                PolicyAreaIds = [.. policyAreas[key].Select(area => area.Value)],
                SourceHost = row.SourceHost,
                EventDate = row.EventDate,
                BillTitle = row.BillTitle,
                LinkType = row.LinkType,
                StatementRecordId = row.StatementRecordId,
                TargetKind = row.TargetKind is null ? null : KindOf(row.TargetKind),
                TargetRecordId = row.TargetRecordId,
                MatchedOfficialId = officials[key].SingleOrDefault(official => official.IsMatched)?.OfficialId,
                DocumentType = row.DocumentType,
                YeaCount = row.YeaCount,
                NayCount = row.NayCount,
                Alignment = row.Alignment,
                CosponsorIds = kind == PublishedRecordKind.Action ? [.. cosponsors[key].Select(cosponsor => cosponsor.Value)] : null
            };
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

    private static object?[] ScalarsOf(PublishedRecordEntry entry) =>
    [
        PublishedRecordReference.NameOf(entry.Kind), entry.RecordId, entry.RevisionNumber, entry.Headline, entry.SourceHost, entry.EventDate,
        entry.BillTitle, entry.LinkType?.ToString(), entry.StatementRecordId, entry.TargetKind is { } target ? PublishedRecordReference.NameOf(target) : null,
        entry.TargetRecordId, entry.DocumentType?.ToString(), entry.YeaCount, entry.NayCount, entry.Alignment?.ToString()
    ];

    private static List<ChildRow<T>> Flatten<T>(List<PublishedRecordEntry> entries, Func<PublishedRecordEntry, IEnumerable<T>> children) =>
        [.. entries.SelectMany(entry => children(entry).Select((value, position) => new ChildRow<T>(entry, (short)position, value)))];

    private static object[] KeysOf<T>(List<ChildRow<T>> rows) =>
    [
        rows.Select(row => PublishedRecordReference.NameOf(row.Entry.Kind)).ToArray(), rows.Select(row => row.Entry.RecordId).ToArray(),
        rows.Select(row => row.Entry.RevisionNumber).ToArray(), rows.Select(row => row.Position).ToArray()
    ];

    private static PublishedRecordKind KindOf(string name) =>
        PublishedRecordReference.TryParseKind(name, out var kind) ? kind : throw new InvalidDataException($"Unknown record kind '{name}'.");

    private readonly record struct ChildRow<T>(PublishedRecordEntry Entry, short Position, T Value);
}
