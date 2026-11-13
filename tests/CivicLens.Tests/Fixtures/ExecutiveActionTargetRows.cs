using System.Globalization;
using System.Text;
using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Tests.Fixtures;

/// <summary>
/// Seeds one source executive action whose notes name six references, and the records that these references meet. The active release is 2.
/// Run it with replica role mode. The targets come from the production rule, so the rows match what a revision stores.
/// </summary>
public static class ExecutiveActionTargetRows
{
    public static readonly string Source = new('a', 64);
    public static readonly string LinkedOrder = new('b', 64);
    public static readonly string SameNumberProclamation = new('c', 64);
    public static readonly string TwinOrderOne = new('d', 64);
    public static readonly string TwinOrderTwo = new('e', 64);
    public static readonly string RetiredOrder = new('f', 64);
    public static readonly string LinkedProclamation = new('1', 64);

    public const string Notes = "Amends: EO 14100, January 3, 2025; EO 14101; EO 14102\nRevokes: Proc. 10001; EO 14000\nSee: EO 5\nAmends: EO 1 and EO 2";

    public static string Seed()
    {
        var sql = new StringBuilder();
        Revision(sql, Source, "14000", ExecutiveActionKind.ExecutiveOrder, last: null, Notes);
        Revision(sql, LinkedOrder, "14100", ExecutiveActionKind.ExecutiveOrder, last: null);
        Revision(sql, SameNumberProclamation, "14100", ExecutiveActionKind.Proclamation, last: null);
        Revision(sql, TwinOrderOne, "14101", ExecutiveActionKind.ExecutiveOrder, last: null);
        Revision(sql, TwinOrderTwo, "14101", ExecutiveActionKind.ExecutiveOrder, last: null);
        Revision(sql, RetiredOrder, "14102", ExecutiveActionKind.ExecutiveOrder, last: 1);
        Revision(sql, LinkedProclamation, "10001", ExecutiveActionKind.Proclamation, last: null);
        var targets = ExecutiveActionDispositionTargets.Parse(Notes);
        for (var position = 0; position < targets.Count; position++)
        {
            var target = targets[position];
            sql.AppendLine(CultureInfo.InvariantCulture, $"""
                INSERT INTO executive_action_revision_disposition_targets (record_id, revision_number, "position", relation, kind, number, reference_start, reference_length)
                  VALUES ('{Source}', 1, {position}, '{target.Relation}', '{target.Kind}', '{target.Number}', {target.Start}, {target.Length});
                """);
        }
        return sql.ToString();
    }

    // The saved record holds the document number line and, when the revision has notes, the notes line. Each cited line has its span.
    private static void Revision(StringBuilder sql, string record, string number, ExecutiveActionKind kind, int? last, string? notes = null)
    {
        var documentLine = $"Federal Register document: 2026-{record[..5]}";
        var notesLine = notes is null ? null : $"\nDisposition notes: {notes}";
        var notesColumns = notesLine is null ? "NULL, NULL, NULL, NULL" : $"'{notes}', '{record}', {documentLine.Length + 1}, {notesLine.Length - 1}";
        sql.AppendLine(CultureInfo.InvariantCulture, $"""
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
              VALUES ('executiveAction', '{record}', 1, 1, {(last is null ? "NULL" : last.ToString())});
            INSERT INTO executive_action_revisions (record_id, revision_number, revision_json, created_at_utc_ticks, author_kind, author_subject, code_version,
                source_id, document_number, kind, presidential_number, title, signed_on, published_on, federal_register_citation, president_name, person_id, document_url,
                disposition_notes, disposition_notes_extraction_id, disposition_notes_start, disposition_notes_length)
              VALUES ('{record}', 1, 'x', 1, 'Code', 'civic-lens:code', '{new string('c', 40)}', 'fr', '2026-{record[..5]}', '{kind}', '{number}', 'Title {number}',
                '2026-01-20', '2026-01-23', '91 FR 1', 'Jane Roe', 'president', 'https://fr.example/{number}', {notesColumns});
            {PublicRecordPageRows.ExecutiveActionSource(record, documentLine + notesLine)}
            INSERT INTO executive_action_revision_facts (record_id, revision_number, "position", citation_extraction_id, citation_start, citation_length)
              VALUES ('{record}', 1, 0, '{record}', 0, {documentLine.Length});
            """);
    }
}
