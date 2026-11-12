using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.Officials;

/// <summary>Reads the bill and issue figures for selected officials and Congresses.</summary>
internal static class OfficialActivityReader
{
    private const int MaximumIssues = 10;

    private static readonly string[] SubstantiveKinds = PublishedVoteKindExtensions.SubstantiveNames();

    public static int? CongressForActivity(PublishedOfficeKind? kind, int? attendanceCongress, long? publishedAtUtcTicks)
    {
        if (attendanceCongress is { } congress) return congress;
        if (kind is not (PublishedOfficeKind.Senator or PublishedOfficeKind.Representative) || publishedAtUtcTicks is not { } ticks)
            return null;
        var publishedOn = DateOnly.FromDateTime(new DateTime(ticks, DateTimeKind.Utc));
        return CongressCalendar.CongressOn(publishedOn);
    }

    private const string StoredBillsSql = """
        WITH selected AS (
            SELECT DISTINCT official_id, congress FROM unnest(@officialIds, @congresses) AS pairs(official_id, congress)
        )
        SELECT selected.official_id, selected.congress, totals.bills, coalesce(members.sponsored, 0), coalesce(members.cosponsored, 0)
          FROM selected
          LEFT JOIN public_api.release_bill_counts totals ON totals.congress = selected.congress
          LEFT JOIN public_api.release_official_bill_counts members
            ON members.official_id = selected.official_id AND members.congress = selected.congress
         ORDER BY selected.official_id
        """;

    private const string IssuesSql = """
        WITH selected AS (
            SELECT DISTINCT official_id, congress FROM unnest(@officialIds, @congresses) AS pairs(official_id, congress)
        ), grouped AS (
            SELECT selected.official_id, selected.congress, counts.issue_id,
                   coalesce(sum(counts.records) FILTER (WHERE counts.source = 'bill'), 0) AS bills,
                   coalesce(sum(counts.records) FILTER (WHERE counts.source = 'vote' AND counts.vote_kind = ANY(@kinds)), 0) AS votes
              FROM selected
              JOIN public_api.official_issue_counts counts
                ON counts.official_id = selected.official_id AND counts.congress = selected.congress
             GROUP BY selected.official_id, selected.congress, counts.issue_id
            HAVING sum(counts.records) FILTER (WHERE counts.source = 'bill' OR counts.vote_kind = ANY(@kinds)) > 0
        ), ranked AS (
            SELECT grouped.*, row_number() OVER (PARTITION BY official_id, congress ORDER BY bills + votes DESC, issue_id) AS position
              FROM grouped
        )
        SELECT ranked.official_id, ranked.congress, ranked.issue_id, i.name, ranked.bills, ranked.votes FROM ranked
          LEFT JOIN public_api.release_issues i ON i.issue_id = ranked.issue_id
         WHERE ranked.position <= @take
         ORDER BY ranked.official_id, ranked.congress, ranked.position
        """;

    public static async Task<Dictionary<(string OfficialId, int Congress), OfficialActivityFigures>> ReadAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyCollection<(string OfficialId, int Congress)> selections, CancellationToken cancellationToken)
    {
        var figures = selections.Distinct().ToDictionary(selection => selection, selection => new MutableFigures());
        if (figures.Count == 0) return [];
        var pairs = figures.Keys.ToArray();
        var ids = pairs.Select(selection => selection.OfficialId).ToArray();
        var congresses = pairs.Select(selection => selection.Congress).ToArray();
        await ReadBillsAsync(connection, transaction, ids, congresses, figures, cancellationToken);
        await ReadIssuesAsync(connection, transaction, ids, congresses, figures, cancellationToken);
        return figures.ToDictionary(pair => pair.Key, pair => pair.Value.ToFigures(pair.Key.Congress));
    }

    /// <summary>Reads the stored bill figures of the active release. A Congress with no stored total has no snapshot, so its figure stays absent and never shows zero.
    /// A missing member row under a stored total means zero.</summary>
    private static async Task ReadBillsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string[] officialIds, int[] congresses,
        Dictionary<(string OfficialId, int Congress), MutableFigures> figures, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(StoredBillsSql, connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("officialIds", officialIds);
        command.Parameters.AddWithValue("congresses", congresses);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(2)) continue;
            var key = (reader.GetString(0), reader.GetInt32(1));
            figures[key].Bills = new(key.Item2, ViewColumns.Number(reader, 3)!.Value, ViewColumns.Number(reader, 4)!.Value,
                ViewColumns.Number(reader, 2)!.Value, OfficialScopes.Bills(key.Item2));
        }
    }

    private static async Task ReadIssuesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string[] officialIds, int[] congresses,
        Dictionary<(string OfficialId, int Congress), MutableFigures> figures, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(IssuesSql, connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("officialIds", officialIds);
        command.Parameters.AddWithValue("congresses", congresses);
        command.Parameters.AddWithValue("kinds", SubstantiveKinds);
        command.Parameters.AddWithValue("take", MaximumIssues);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = (reader.GetString(0), reader.GetInt32(1));
            figures[key].Issues.Add(new(reader.GetString(2), ViewColumns.Number(reader, 4)!.Value, ViewColumns.Number(reader, 5)!.Value)
            {
                Name = ViewColumns.Text(reader, 3)
            });
        }
    }

    private sealed class MutableFigures
    {
        public BillCountsFigure? Bills { get; set; }
        public List<IssueCountsFigure> Issues { get; } = [];

        public OfficialActivityFigures ToFigures(int congress) => new(Bills, new(congress, OfficialScopes.Issues(congress), [.. Issues]));
    }
}

internal sealed record OfficialActivityFigures(BillCountsFigure? Bills, IssueCountsFigures Issues)
{
    /// <summary>
    /// True when the reader selected a Congress for the official but the release holds no stored bill count for it. The issue figures exist exactly when a Congress
    /// is selected, because <see cref="OfficialActivityReader"/> always builds them for one. An official with no Congress shows no bill figure and no caption.
    /// </summary>
    public static bool BillCountsAbsent(BillCountsFigure? bills, IssueCountsFigures? issues) => issues is not null && bills is null;
}
