using System.Globalization;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Officials;

internal sealed record SeededTerm(string Office, string Kind, string StartsOn)
{
    public string? State { get; init; }
    public int? District { get; init; }
    public string? Party { get; init; }
    public string? Caucus { get; init; }
    public string? EndsOn { get; init; }
}

/// <summary>An official of release 1 as the name tables hold it. The position of a term is its index.</summary>
internal sealed record SeededOfficial(string Id, string Name, params SeededTerm[] Terms);

/// <summary>A published entry that names officials, as the entry tables hold it. Only the kinds that the officials views read are supported.</summary>
internal sealed record SeededEntry(string Kind, char Id, params string[] Officials)
{
    public string? DocumentType { get; init; }
    public int FirstRelease { get; init; } = 1;
    public int? LastRelease { get; init; }

    public string RecordId { get; init; } = new(Id, 64);
}

internal static class OfficialsSeed
{
    public static async Task AddOfficialsAsync(string database, params SeededOfficial[] officials)
    {
        foreach (var official in officials)
            await ViewsSeed.ExecuteAsync(database, $"""
                INSERT INTO release_officials (release_number, official_id, name) VALUES (1, '{official.Id}', '{official.Name.Replace("'", "''")}')
                  ON CONFLICT (release_number, official_id) DO UPDATE SET name = EXCLUDED.name;
                {string.Join('\n', official.Terms.Select((term, position) => TermRow(official.Id, position, term)))}
                """);
    }

    public static async Task AddEntriesAsync(string database, params SeededEntry[] entries)
    {
        foreach (var entry in entries) await ViewsSeed.ExecuteAsync(database, Insert(entry));
    }

    private static string Text(string? value) => value is null ? "NULL" : $"'{value}'";

    private static string TermRow(string official, int position, SeededTerm term) => $"""
        INSERT INTO release_official_terms (release_number, official_id, "position", office, kind, state, district, party, caucus, start_on, end_on)
        VALUES (1, '{official}', {position}, '{term.Office}', '{term.Kind}', {Text(term.State)}, {(term.District is { } district ? district.ToString(CultureInfo.InvariantCulture) : "NULL")},
                {Text(term.Party)}, {Text(term.Caucus)}, '{term.StartsOn}', {Text(term.EndsOn)});
        """;

    private static string Keys(SeededEntry entry) => entry.Kind switch
    {
        "statement" => $"statement_candidate_id, statement_revision_number) VALUES ('statement', '{entry.RecordId}', 1, 'H', '{entry.RecordId}', 1",
        "executiveAction" => $"executive_action_record_id, executive_action_revision_number, document_type) VALUES ('executiveAction', '{entry.RecordId}', 1, 'H', '{entry.RecordId}', 1, '{entry.DocumentType}'",
        _ => throw new ArgumentException($"The seed does not write the entry kind {entry.Kind}.")
    };

    private static string Insert(SeededEntry entry)
    {
        var last = entry.LastRelease?.ToString(CultureInfo.InvariantCulture) ?? "NULL";
        return $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
              VALUES ('{entry.Kind}', '{entry.RecordId}', {entry.FirstRelease}, 1, {last});
            INSERT INTO published_entries (kind, record_id, revision_number, headline, {Keys(entry)});
            {string.Join('\n', entry.Officials.Select((official, position) => $"""
                INSERT INTO published_entry_officials (kind, record_id, revision_number, "position", official_id)
                VALUES ('{entry.Kind}', '{entry.RecordId}', 1, {position}, '{official}');
                """))}
            """;
    }
}
