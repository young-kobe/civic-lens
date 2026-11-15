using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicWeb.Publication;
using Npgsql;

namespace CivicLens.PublicApi.Briefs;

/// <summary>Reads one page of the weekly briefs list in one statement, so the release, the time, the total, and the rows share one snapshot.
/// The order is newest week first, then record ID. <see cref="PublicationProtocol.PageSize"/> briefs make a page.
/// The list has no page while the release holds no brief, so the empty list answers the not found page and no link or sitemap entry names it.</summary>
public sealed class BriefListReader(NpgsqlDataSource views)
{
    private const string Sql = """
        SELECT a.release_number, a.published_at_utc_ticks, (SELECT count(*) FROM public_api.briefs),
               b.record_id, b.week_start, b.headline, b.label, b.first_published_at_utc_ticks
          FROM (SELECT release_number, published_at_utc_ticks FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT * FROM public_api.briefs ORDER BY week_start DESC, record_id LIMIT @take OFFSET @skip
          ) b ON TRUE
         ORDER BY b.week_start DESC, b.record_id
        """;

    private const string ExistsSql = "SELECT EXISTS (SELECT 1 FROM public_api.briefs)";

    /// <summary>The number of list pages of a release that holds the given number of briefs. The one owner of the rule that no brief means no page.</summary>
    public static int PageCount(long briefs) => briefs == 0 ? 0 : PublicationProtocol.PageCount(checked((int)briefs));

    /// <summary>True when the active release holds a weekly brief, so a page may link the briefs list.</summary>
    public async Task<bool> ExistsAsync(CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(ExistsSql);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>Reads one page. Null means that no release is active, that the release holds no brief, or that the page lies past the last brief.</summary>
    public async Task<BriefsPageData?> ReadAsync(int page, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("take", PublicationProtocol.PageSize);
        // The skip is a long, so a large page number reads past the end and answers 404 instead of an int overflow.
        command.Parameters.AddWithValue("skip", (page - 1L) * PublicationProtocol.PageSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var release = reader.GetInt32(0);
        var publishedAt = new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero);
        var total = checked((int)reader.GetInt64(2));
        if (page > PageCount(total)) return null;
        var briefs = new List<BriefListItemData>();
        do
        {
            if (reader.IsDBNull(3)) continue;
            briefs.Add(new(reader.GetString(3), reader.GetFieldValue<DateOnly>(4), reader.GetString(5), ViewColumns.Text(reader, 6),
                reader.IsDBNull(7) ? null : new DateTimeOffset(reader.GetInt64(7), TimeSpan.Zero)));
        }
        while (await reader.ReadAsync(cancellationToken));
        return new(release, publishedAt, page, total, [.. briefs]);
    }
}
