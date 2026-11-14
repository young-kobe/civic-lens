using System.Data;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Sitemap;

/// <summary>Reads the public paths of the sitemap from the <c>public_api</c> views, inside one repeatable-read snapshot, so the counts and the paths come from one release.
/// The fixed pages and the weekly briefs pages come first, then the official, official record, issue, record, and vote map pages, each in ID order.
/// A request reads only the paths of its own file, at most <see cref="MaximumUrls"/>. The sitemap index needs only the counts.</summary>
public sealed class SitemapReader(NpgsqlDataSource views)
{
    /// <summary>The limit of the sitemap protocol: one sitemap file holds at most this many addresses.</summary>
    public const int MaximumUrls = 50_000;

    // Each source is one page type. The source number orders the paths and selects the path builder in PathOf. Both statements read this one table expression.
    private const string Paths = """
        SELECT 0 AS source, official_id AS id FROM public_api.release_officials
        UNION ALL SELECT 1, official_id FROM public_api.release_officials
        UNION ALL SELECT 2, issue_id FROM public_api.release_issues
        UNION ALL SELECT 3, record_id FROM (SELECT DISTINCT record_id FROM public_api.active_records) r
        UNION ALL SELECT 4, record_id FROM (SELECT DISTINCT record_id FROM public_api.active_records WHERE kind = @vote) v
        """;

    private const string CountsSql = "SELECT a.release_number, (SELECT count(*) FROM public_api.briefs), (SELECT count(*) FROM (" + Paths + ") paths) " +
        "FROM public_api.active_release a LIMIT 1";

    private const string PathsSql = "SELECT source, id FROM (" + Paths + """) paths ORDER BY source, id COLLATE "C" LIMIT @take OFFSET @skip""";

    /// <summary>The pages that every release has. Search, report, and the not found page are left out, because a reader must not land on them from a search engine.</summary>
    private static readonly string[] FixedPaths =
    [
        string.Empty,
        PublicationProtocol.OfficialsPagePath,
        PublicationProtocol.BoardsPagePath,
        .. OfficialBoard.All.Select(board => board.PagePath),
        PublicationProtocol.FacetsPath,
        .. PublicSections.RecordLists.Select(list => list.Path),
        PublicationProtocol.ComparePagePath,
        PublicationProtocol.VoteKindsPagePath,
        PublicationProtocol.GlossaryPagePath,
        PublicationProtocol.MethodPagePath,
        PublicationProtocol.AboutPagePath
    ];

    /// <summary>Reads the sitemap. Null file means <c>sitemap.xml</c>: it gets the paths only when one file holds them all, and is the index otherwise.
    /// A numbered file gets its paths only when the site needs an index and the file exists. Null means that no release is active.</summary>
    public async Task<SitemapData?> ReadAsync(int? file, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (await ReadCountsAsync(connection, transaction, cancellationToken) is not var (release, briefs, rows)) return null;
        var head = HeadPaths(briefs);
        var total = head.Count + rows;
        var fileCount = (int)Math.Max(1, (total + MaximumUrls - 1) / MaximumUrls);
        var slice = file switch
        {
            null when fileCount == 1 => 1,
            { } number when fileCount > 1 && number <= fileCount => number,
            _ => (int?)null
        };
        if (slice is not { } index) return new(release, fileCount, []);
        // The file covers the addresses from start to end. The head paths fill its first part, and the view rows fill the rest.
        var start = (index - 1L) * MaximumUrls;
        var end = Math.Min(start + MaximumUrls, total);
        var headStart = (int)Math.Min(start, head.Count);
        var paths = head.GetRange(headStart, (int)Math.Min(end, head.Count) - headStart);
        var rowStart = Math.Max(start, head.Count);
        if (end > rowStart) paths.AddRange(await ReadPathsAsync(connection, transaction, rowStart - head.Count, end - rowStart, cancellationToken));
        return new(release, fileCount, paths);
    }

    private static List<string> HeadPaths(long briefs) =>
        [.. FixedPaths, .. Enumerable.Range(1, PublicationProtocol.PageCount(checked((int)briefs))).Select(PublicationProtocol.BriefsPagePath)];

    private static async Task<(int Release, long Briefs, long Rows)?> ReadCountsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CountsSql, connection, transaction);
        command.Parameters.AddWithValue("vote", PublicationProtocol.KindText(PublishedRecordKind.Vote));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetInt32(0), reader.GetInt64(1), reader.GetInt64(2)) : null;
    }

    private static async Task<List<string>> ReadPathsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, long skip, long take,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PathsSql, connection, transaction);
        command.Parameters.AddWithValue("vote", PublicationProtocol.KindText(PublishedRecordKind.Vote));
        command.Parameters.AddWithValue("skip", skip);
        command.Parameters.AddWithValue("take", take);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var paths = new List<string>();
        while (await reader.ReadAsync(cancellationToken)) paths.Add(PathOf(reader.GetInt32(0), reader.GetString(1)));
        return paths;
    }

    private static string PathOf(int source, string id) => source switch
    {
        0 => PublicationProtocol.OfficialPagePath(id),
        1 => PublicationProtocol.OfficialRecordPagePath(id),
        2 => PublicationProtocol.IssuePagePath(id, 1),
        3 => PublicationProtocol.RecordPagePath(id),
        4 => PublicationProtocol.VoteMapPagePath(id),
        _ => throw new InvalidOperationException($"The sitemap has no path source {source}.")
    };
}
