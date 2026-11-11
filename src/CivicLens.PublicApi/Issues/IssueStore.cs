using System.Data;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Issues;

/// <summary>Reads the views <c>public_api.release_issues</c>, <c>active_release</c>, <c>issue_votes</c>, <c>bill_issues</c>, and <c>issue_sponsors</c> for one issue.
/// One snapshot serves both statements, so every count comes from one release, as in <c>ZipDistrictStore</c>.
/// The kind rule is <see cref="PublishedVoteKindExtensions.SubstantiveNames"/>. A sponsor is a sponsorship with the role <c>Sponsor</c>, as in <c>bills.sponsor_count</c>.
/// The current Congress is the Congress of the publication day of the release, from <see cref="CongressCalendar"/>.
/// The line-up reads <c>release_issue_party_lineups</c> in the same snapshot, and its base is the Home tile count <c>release_issues.bills</c>.</summary>
public sealed class IssueStore(NpgsqlDataSource views)
{
    /// <summary>The most officials in one answer. The issue page shows 5.</summary>
    public const int MaximumSponsors = 5;

    private const string CountsSql = """
        SELECT i.release_number, a.published_at_utc_ticks, i.count_congress, i.bills,
               (SELECT count(*) FROM public_api.issue_votes x WHERE x.issue_id = i.issue_id AND x.kind = ANY(@kinds)),
               (SELECT count(*) FROM public_api.bill_issues x WHERE x.issue_id = i.issue_id)
          FROM public_api.release_issues i
          LEFT JOIN (SELECT published_at_utc_ticks FROM public_api.active_release LIMIT 1) a ON TRUE
         WHERE i.issue_id = @issue
        """;

    private const string LineUpSql = """
        SELECT unknown_bills, party, bills FROM public_api.release_issue_party_lineups WHERE issue_id = @issue ORDER BY bills DESC NULLS LAST, party
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
        var lineUp = await ReadLineUpAsync(connection, issueId, counts, cancellationToken);
        return new(counts.Release, issueId, counts.SubstantiveVotes, counts.Bills, [.. sponsors.Select(sponsor => new IssueSponsor(sponsor.OfficialId, sponsor.Bills, counts.Bills))], lineUp);
    }

    private sealed record Counts(int Release, DateOnly? PublishedOn, int? HomeCongress, int? HomeBills, int SubstantiveVotes, int Bills);

    private static async Task<Counts?> ReadCountsAsync(NpgsqlConnection connection, string issueId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(CountsSql, connection);
        command.Parameters.AddWithValue("issue", issueId);
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        DateOnly? publishedOn = reader.IsDBNull(1) ? null : DateOnly.FromDateTime(new DateTime(reader.GetInt64(1), DateTimeKind.Utc));
        return new(reader.GetInt32(0), publishedOn, reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetInt32(3),
            (int)reader.GetInt64(4), (int)reader.GetInt64(5));
    }

    /// <summary>Null means that the release has no line-up row for the issue. Rows that do not add up to the Home tile count fail the request, so a wrong split never shows.</summary>
    private static async Task<IssuePartyLineUp?> ReadLineUpAsync(NpgsqlConnection connection, string issueId, Counts counts, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(LineUpSql, connection);
        command.Parameters.AddWithValue("issue", issueId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var unknown = reader.GetInt32(0);
        var parties = new List<IssuePartyCount>();
        do
        {
            if (!reader.IsDBNull(1)) parties.Add(new(reader.GetString(1), reader.GetInt32(2)));
        } while (await reader.ReadAsync(cancellationToken));
        if (counts.HomeCongress is not { } congress || counts.HomeBills is not { } bills || bills != unknown + parties.Sum(part => part.Bills))
            throw new InvalidDataException($"The party line-up of issue {issueId} does not add up to the bill count of release {counts.Release}.");
        return new(congress, bills, [.. parties], unknown);
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
