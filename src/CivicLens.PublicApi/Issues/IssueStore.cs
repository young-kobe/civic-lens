using System.Data;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Issues;

/// <summary>Reads the views <c>public_api.release_issues</c>, <c>active_records</c>, <c>issue_votes</c>, <c>bill_issues</c>, and <c>issue_sponsors</c> for one issue.
/// One snapshot serves both statements, so every count comes from one release, as in <c>ZipDistrictStore</c>.
/// The kind rule is <see cref="PublishedVoteKindExtensions.SubstantiveNames"/>. A sponsor is a sponsorship with the role <c>Sponsor</c>, as in <c>bills.sponsor_count</c>.
/// The current Congress is the Congress of the publication day of the release, from <see cref="CongressCalendar"/>.</summary>
public sealed class IssueStore(NpgsqlDataSource views)
{
    /// <summary>The most officials in one answer. The issue page shows 5.</summary>
    public const int MaximumSponsors = 5;

    private const string CountsSql = """
        SELECT i.release_number, a.published_at_utc_ticks,
               (SELECT count(*) FROM public_api.issue_votes x WHERE x.issue_id = i.issue_id AND x.kind = ANY(@kinds)),
               (SELECT count(*) FROM public_api.bill_issues x WHERE x.issue_id = i.issue_id)
          FROM public_api.release_issues i
          LEFT JOIN (SELECT published_at_utc_ticks FROM public_api.active_records LIMIT 1) a ON TRUE
         WHERE i.issue_id = @issue
        """;

    private const string SponsorsSql = """
        SELECT x.official_id, count(*) AS sponsored
          FROM public_api.issue_sponsors x
         WHERE x.issue_id = @issue AND x.event_date >= @congressStart AND x.event_date < @congressEnd
         GROUP BY x.official_id
         ORDER BY sponsored DESC, x.official_id
         LIMIT @take
        """;

    /// <summary>Reads the counts. Null means that the active release holds no such issue.</summary>
    public async Task<IssueCounts?> FindAsync(string issueId, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (await ReadCountsAsync(connection, issueId, cancellationToken) is not { } counts) return null;
        var sponsors = counts.PublishedOn is { } day ? await ReadSponsorsAsync(connection, issueId, day, cancellationToken) : [];
        return new(counts.Release, issueId, counts.SubstantiveVotes, counts.Bills, [.. sponsors.Select(sponsor => new IssueSponsor(sponsor.OfficialId, sponsor.Bills, counts.Bills))]);
    }

    private static async Task<(int Release, DateOnly? PublishedOn, int SubstantiveVotes, int Bills)?> ReadCountsAsync(NpgsqlConnection connection, string issueId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CountsSql, connection);
        command.Parameters.AddWithValue("issue", issueId);
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        DateOnly? publishedOn = reader.IsDBNull(1) ? null : DateOnly.FromDateTime(new DateTime(reader.GetInt64(1), DateTimeKind.Utc));
        return (reader.GetInt32(0), publishedOn, (int)reader.GetInt64(2), (int)reader.GetInt64(3));
    }

    private static async Task<List<(string OfficialId, int Bills)>> ReadSponsorsAsync(NpgsqlConnection connection, string issueId, DateOnly publishedOn,
        CancellationToken cancellationToken)
    {
        var sponsors = new List<(string OfficialId, int Bills)>();
        if (CongressCalendar.CongressOn(publishedOn) is not { } congress) return sponsors;
        await using var command = new NpgsqlCommand(SponsorsSql, connection);
        command.Parameters.AddWithValue("issue", issueId);
        command.Parameters.AddWithValue("congressStart", CongressCalendar.StartOf(congress));
        command.Parameters.AddWithValue("congressEnd", CongressCalendar.StartOf(congress + 1));
        command.Parameters.AddWithValue("take", MaximumSponsors);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) sponsors.Add((reader.GetString(0), (int)reader.GetInt64(1)));
        return sponsors;
    }
}
