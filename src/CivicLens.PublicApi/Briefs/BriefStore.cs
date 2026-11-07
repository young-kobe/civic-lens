using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.Briefs;

/// <summary>Reads the active brief and bounded content from the owner-controlled public views in one statement.</summary>
public sealed class BriefStore(NpgsqlDataSource views)
{
    private const string Sql = """
        SELECT a.release_number, b.record_id, b.revision_number, b.week_start, b.headline, b.text_origin, b.label,
               (SELECT json_agg(json_build_object('text', p.paragraph_text, 'citationIds', p.citation_ids) ORDER BY p.position)
                  FROM public_api.brief_paragraphs p WHERE p.release_number = b.release_number AND p.record_id = b.record_id AND p.revision_number = b.revision_number),
               (SELECT json_agg(json_build_object('id', c.citation_id, 'kind', c.kind, 'recordId', c.target_record_id,
                    'revisionNumber', c.target_revision_number, 'headline', c.headline) ORDER BY c.position)
                  FROM public_api.brief_citations c WHERE c.release_number = b.release_number AND c.record_id = b.record_id AND c.revision_number = b.revision_number)
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
          LEFT JOIN public_api.briefs b ON b.record_id = @id
        """;

    public async Task<BriefDetail?> FindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("id", recordId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0)) return null;
        if (reader.IsDBNull(1)) return null;
        var brief = new BriefRow
        {
            RecordId = reader.GetString(1), RevisionNumber = reader.GetInt32(2), WeekStart = reader.GetFieldValue<DateOnly>(3),
            Headline = reader.GetString(4), TextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 5), Label = ViewColumns.Text(reader, 6)
        };
        if (reader.IsDBNull(7) || reader.IsDBNull(8))
            throw new InvalidDataException("The active brief has no published content.");
        var paragraphs = System.Text.Json.JsonSerializer.Deserialize<PublishedBriefParagraph[]>(reader.GetString(7), PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("The brief paragraphs view returned invalid content.");
        var citations = System.Text.Json.JsonSerializer.Deserialize<PublishedBriefCitation[]>(reader.GetString(8), PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("The brief citations view returned invalid content.");
        PublishedBrief.ValidateContent(paragraphs, citations);
        return new(reader.GetInt32(0), brief, paragraphs, citations);
    }

}
