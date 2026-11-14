using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using Npgsql;

namespace CivicLens.PublicApi.Relationships;

/// <summary>Reads one active relationship from <c>public_api.relationships</c> with the officials, issues, and times of its published entry, in one statement.
/// The quote span counts UTF-16 code units, so the store cuts the quote from the cited text in C#, as the search ingest does.</summary>
public sealed class RelationshipStore
{
    private const int LabelLimit = PublishedRecordEntry.MaximumFilterValues + 1;

    private const string Sql = """
        SELECT a.release_number, r.record_id, r.revision_number, r.headline, r.link_type, r.statement_date, r.reason, r.text_origin, r.label,
               r.statement_record_id, r.statement_revision_number, r.statement_headline, r.statement_text_origin, r.statement_label,
               r.target_kind, r.target_record_id, r.target_revision_number, r.target_headline, r.target_text_origin, r.target_label,
               r.quote_start, r.quote_length, r.quote_source_text, r.fact, r.fact_label, r.fact_text_origin,
               ARRAY(SELECT o.official_id FROM public_api.record_officials o
                      WHERE o.kind = 'relationship' AND o.record_id = r.record_id AND o.revision_number = r.revision_number ORDER BY o."position" LIMIT @label_limit),
               ARRAY(SELECT i.issue_id FROM public_api.record_issues i
                      WHERE i.kind = 'relationship' AND i.record_id = r.record_id AND i.revision_number = r.revision_number ORDER BY i."position" LIMIT @label_limit),
               e.approved_at_utc_ticks, f.first_published_at_utc_ticks
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN public_api.relationships r ON r.record_id = @id
          LEFT JOIN public_api.record_entries e ON e.kind = 'relationship' AND e.record_id = r.record_id AND e.revision_number = r.revision_number
          LEFT JOIN public_api.record_first_publications f ON f.kind = 'relationship' AND f.record_id = r.record_id
        """;

    /// <summary>Reads the relationship inside the transaction of the caller, so that it shares a snapshot with the other reads of that transaction.
    /// A null detail means that the active release holds no relationship with this ID, or that one of its ends is not active.</summary>
    public async Task<(int? Release, RelationshipDetail? Detail)> FindAsync(NpgsqlTransaction transaction, string recordId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(Sql, transaction.Connection, transaction);
        command.Parameters.AddWithValue("id", recordId);
        command.Parameters.AddWithValue("label_limit", LabelLimit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return (null, null);
        var release = reader.GetInt32(0);
        if (reader.IsDBNull(1)) return (release, null);
        var page = new RelationshipPageData(release, reader.GetString(1), reader.GetInt32(2), Required(reader, 3), ViewColumns.Enum<PublishedRelationshipType>(reader, 4),
            ViewColumns.Date(reader, 5), Required(reader, 6), ViewColumns.Enum<PublishedTextOrigin>(reader, 7), ViewColumns.Text(reader, 8),
            ReadEnd(reader, PublishedRecordKind.Statement, 9), ReadEnd(reader, TargetKind(reader), 15), ReadQuote(reader), Required(reader, 23), ViewColumns.Text(reader, 24),
            ViewColumns.NullableEnum<PublishedTextOrigin>(reader, 25), [], [], ViewColumns.Utc(reader, 28), ViewColumns.Utc(reader, 29));
        var detail = new RelationshipDetail(page, reader.GetFieldValue<string[]>(26), reader.GetFieldValue<string[]>(27));
        Validate(detail);
        return (release, detail);
    }

    // The columns of one end, from its record ID on: record ID, cited revision, current headline, text origin, label.
    private static RelationshipEndData ReadEnd(NpgsqlDataReader reader, PublishedRecordKind kind, int ordinal) => new(kind, reader.GetString(ordinal),
        reader.GetInt32(ordinal + 1), Required(reader, ordinal + 2), ViewColumns.Enum<PublishedTextOrigin>(reader, ordinal + 3), ViewColumns.Text(reader, ordinal + 4));

    private static PublishedRecordKind TargetKind(NpgsqlDataReader reader) => RecordKinds.TryParse(ViewColumns.Text(reader, 14), out var kind)
        ? kind
        : throw new InvalidDataException("The relationship view holds an unknown target kind.");

    private static string ReadQuote(NpgsqlDataReader reader) =>
        PublishedDocumentVersion.QuoteAt(Required(reader, 22), reader.GetInt32(20), reader.GetInt32(21))
        ?? throw new InvalidDataException("The relationship quote does not match its offsets.");

    private static string Required(NpgsqlDataReader reader, int ordinal) =>
        ViewColumns.Text(reader, ordinal) is { } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidDataException("The relationship view has no text for a required column.");

    private static void Validate(RelationshipDetail detail)
    {
        var page = detail.Page;
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Relationship, page.RecordId) || page.RevisionNumber < 1 ||
            page.Target.Kind is not (PublishedRecordKind.Action or PublishedRecordKind.Vote) ||
            page.Statement.RevisionNumber < 1 || page.Target.RevisionNumber < 1)
            throw new InvalidDataException("Published relationship identity is invalid.");
        if (page.Reason.Length > PublishedRelationship.MaximumReasonLength || page.Quote.Length > PublishedRelationship.MaximumQuoteLength ||
            page.Fact.Length > PublishedRelationship.MaximumFactLength)
            throw new InvalidDataException("Published relationship text exceeds its maximum length.");
        if (detail.OfficialIds.Length >= LabelLimit || detail.IssueIds.Length >= LabelLimit)
            throw new InvalidDataException("Published relationship exceeds its label limit.");
    }
}
