using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.Tests.PublicApi;

/// <summary>
/// The shape of release 1096 of production: with the templates, the active release holds 21,944 revisions and 19,627 bills, and names 553 officials.
/// <see cref="Templates"/> gives one record of each kind. After a test publishes them, <see cref="CopyAsync"/> repeats each template at its production count.
/// The ingest read test and the list scale test both read this release.
/// </summary>
internal static class ProductionSizeRelease
{
    public const int Release = 1096;
    public const int Officials = 540;
    public const int Actions = 19_626;
    public const int Votes = 1_499;
    public const int Statements = 599;
    public const int ExecutiveActions = 216;

    public static SampleRecord[] Templates()
    {
        var action = PublishedSamples.Action("Doe sponsors a bill", "Doe sponsored it.", "A housing bill", "Sponsor: D000001 and more.");
        var executive = PublishedSamples.ExecutiveAction("Ending the Weaponization", "Tail text.", ["The order ends a program.", "It starts today."]);
        var executiveId = new string('d', 64);
        return
        [
            PublishedSamples.Statement('1', "Rent cap", issues: ["housing", "budget"], areas: ["economics"]),
            action with { Entry = action.Entry with { BillLine = "This bill funds housing." } },
            PublishedSamples.Vote("Passage", "The House passed it.", "Roll call raw text."),
            executive with
            {
                Entry = executive.Entry with { RecordId = executiveId },
                Body = ((PublishedExecutiveAction)executive.Body) with { RecordId = executiveId }
            }
        ];
    }

    // A bill of production has about 13 sponsorship citations over 2 sources. The template bill takes 12 cosponsors, and every other cosponsor cites a second source.
    public static string CosponsorsSql(string recordId) => $"""
        SET session_replication_role = replica;
        INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
        SELECT 'c' || substr(extraction_id, 2), attempt_id, extraction_version, 'Cosponsors: ' || repeat('C000001 joined the bill. ', 400), text_sha256
          FROM document_extractions
         WHERE extraction_id = (SELECT citation_extraction_id FROM action_revision_sponsorship_citations WHERE record_id = '{recordId}');
        INSERT INTO action_revision_sponsorships (record_id, revision_number, "position", person_id, bioguide_id, role, is_original, sponsored_on, withdrawn_on)
        SELECT record_id, revision_number, n, 'c' || n, 'C' || n, 'Cosponsor', false, sponsored_on, NULL
          FROM action_revision_sponsorships, generate_series(1, 12) n WHERE record_id = '{recordId}';
        INSERT INTO action_revision_sponsorship_citations (record_id, revision_number, sponsorship_position, "position", citation_extraction_id, citation_start, citation_length)
        SELECT record_id, revision_number, n, 0, CASE WHEN n % 2 = 0 THEN 'c' || substr(citation_extraction_id, 2) ELSE citation_extraction_id END, citation_start, citation_length
          FROM action_revision_sponsorship_citations, generate_series(1, 12) n WHERE record_id = '{recordId}';
        INSERT INTO release_officials (release_number, official_id, name) SELECT 1, 'c' || n, 'Official c' || n FROM generate_series(1, 12) n;
        """;

    public static int CopiesOf(PublishedRecordKind kind) => kind switch
    {
        PublishedRecordKind.Action => Actions,
        PublishedRecordKind.Vote => Votes,
        PublishedRecordKind.Statement => Statements,
        PublishedRecordKind.ExecutiveAction => ExecutiveActions,
        _ => throw new InvalidOperationException("The scale test copies no record of this kind.")
    };

    // The columns that hold the record ID in the tables of the entries and the typed revisions. An entry repeats its record ID in the key column of its kind.
    private static readonly string[] IdColumns = ["record_id", "candidate_id", "statement_candidate_id", "action_record_id", "vote_record_id", "executive_action_record_id"];

    // Each copy repeats the rows of its template under the record ID md5(kind || n) || md5('x' || n), as the seed of the vote list plan test does.
    // The copies share the source rows of their template. The seed then gives the data the shape of production:
    // many releases, record states that start in different releases, some closed states, and more officials in the active release.
    public static async Task CopyAsync(string database, SampleRecord[] templates)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        var tables = await TablesAsync(connection);
        List<string> copies = [];
        foreach (var template in templates)
        {
            var kind = PublicationProtocol.KindText(template.Entry.Kind);
            copies.Add($"""
                INSERT INTO copy_ids (template, id)
                SELECT '{template.Entry.RecordId}', md5('{kind}' || n::text) || md5('x' || n::text) FROM generate_series(1, {CopiesOf(template.Entry.Kind)}) n
                """);
        }
        copies.AddRange(tables.Select(CopySql));
        foreach (var statement in (string[])[
            "SET session_replication_role = replica",
            "CREATE TEMP TABLE copy_ids (template text NOT NULL, id text NOT NULL)",
            DropSharedUniqueIndexesSql(tables),
            .. copies,
            $"""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active, code_version)
            SELECT n, lpad(n::text, 6, '0') || '-' || md5(n::text), actor_subject, published_at_utc_ticks + n, record_count, false, code_version
              FROM publication_releases, generate_series(2, {Release}) n WHERE release_number = 1
            """,
            "UPDATE publication_releases SET is_active = false WHERE release_number = 1",
            $"UPDATE publication_releases SET is_active = true WHERE release_number = {Release}",
            $"""
            INSERT INTO release_officials (release_number, official_id, name)
            SELECT {Release}, official_id, name FROM release_officials WHERE release_number = 1
             UNION ALL SELECT {Release}, 'x' || n, 'Official x' || n FROM generate_series(1, {Officials}) n
            """,
            $"UPDATE publication_record_states SET first_release = 1 + abs(hashtext(record_id)) % {Release}",
            """
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
            SELECT kind, record_id, 1, revision_number, first_release - 1 FROM publication_record_states
             WHERE first_release > 1 AND abs(hashtext(record_id)) % 8 = 0
            """,
            $"ANALYZE {string.Join(", ", tables.Select(table => table.Name))}, publication_releases, release_officials"])
        {
            await using var command = new NpgsqlCommand(statement, connection) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync();
        }
    }

    // The copies share the run, the document number, and the roll call of their template. So the unique indexes without an ID column go.
    // The views find the rows of a record by its ID, so the plans of the views do not use these indexes.
    private static string DropSharedUniqueIndexesSql(IdTable[] tables) => $$"""
        DO $drop$
        DECLARE unique_index record;
        BEGIN
          FOR unique_index IN
            SELECT i.indexrelid::regclass AS name, i.indrelid::regclass AS owner, c.conname
              FROM pg_index i
              LEFT JOIN pg_constraint c ON c.conindid = i.indexrelid
             WHERE i.indisunique AND NOT i.indisprimary
               AND i.indrelid::regclass::text IN ({{string.Join(", ", tables.Select(table => $"'{table.Name}'"))}})
               AND NOT EXISTS (SELECT 1 FROM pg_attribute a WHERE a.attrelid = i.indrelid AND a.attnum = ANY(i.indkey) AND a.attname = ANY(ARRAY['{{string.Join("', '", IdColumns)}}']))
          LOOP
            IF unique_index.conname IS NULL THEN EXECUTE format('DROP INDEX %s', unique_index.name);
            ELSE EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I', unique_index.owner, unique_index.conname);
            END IF;
          END LOOP;
        END $drop$
        """;

    private sealed record IdTable(string Name, string Columns, string[] IdColumns);

    // The tables that hold a record ID column, with their stored columns only, because published_entries has a generated column.
    private static async Task<IdTable[]> TablesAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("""
            SELECT table_name, string_agg(quote_ident(column_name), ', ' ORDER BY ordinal_position),
                   array_agg(column_name::text) FILTER (WHERE column_name = ANY(@ids))
              FROM information_schema.columns
             WHERE table_schema = 'public' AND is_generated = 'NEVER'
               AND table_name IN (SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE')
             GROUP BY table_name
            HAVING bool_or(column_name = ANY(@ids))
            """, connection);
        command.Parameters.AddWithValue("ids", IdColumns);
        var tables = new List<IdTable>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) tables.Add(new(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<string[]>(2)));
        return [.. tables];
    }

    // A row of a template holds its ID in one or more ID columns. Each ID column that holds the template ID takes the copy ID.
    private static string CopySql(IdTable table)
    {
        var replaced = string.Join(" || ", table.IdColumns.Select(column => $"CASE WHEN t.{column} = c.template THEN jsonb_build_object('{column}', c.id) ELSE '{{}}' END"));
        return $"""
            INSERT INTO {table.Name} ({table.Columns})
            SELECT {string.Join(", ", table.Columns.Split(", ").Select(column => "x." + column))} FROM {table.Name} t
              JOIN copy_ids c ON c.template IN ({string.Join(", ", table.IdColumns.Select(column => "t." + column))}),
                   LATERAL jsonb_populate_record(NULL::{table.Name}, to_jsonb(t) || {replaced}) x
            """;
    }
}
