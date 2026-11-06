using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Issues;

/// <summary>Reads the views <c>public_api.release_issues</c>, <c>issue_votes</c>, <c>bill_issues</c>, and <c>issue_sponsors</c> for one issue. One statement reads the release and every count, so all come from one release.
/// The kind rule is <see cref="PublishedVoteKindExtensions.IsSubstantive"/>. A sponsor is a sponsorship with the role <c>Sponsor</c>, as in <c>bills.sponsor_count</c>.</summary>
public sealed class IssueStore(NpgsqlDataSource views)
{
    /// <summary>The most officials in one answer.</summary>
    public const int MaximumSponsors = 5;

    private static readonly string[] SubstantiveKinds = [.. Enum.GetValues<PublishedVoteKind>().Where(kind => kind.IsSubstantive()).Select(kind => kind.ToString())];

    private const string Sql = """
        SELECT i.release_number, v.votes, v.substantive, (SELECT count(*) FROM public_api.bill_issues x WHERE x.issue_id = i.issue_id),
               s.official_id, s.sponsored
          FROM public_api.release_issues i
         CROSS JOIN LATERAL (
               SELECT count(*) AS votes, count(*) FILTER (WHERE x.kind = ANY(@kinds)) AS substantive
                 FROM public_api.issue_votes x WHERE x.issue_id = i.issue_id) v
          LEFT JOIN LATERAL (
               SELECT x.official_id, count(*) AS sponsored
                 FROM public_api.issue_sponsors x WHERE x.issue_id = i.issue_id
                GROUP BY x.official_id ORDER BY sponsored DESC, x.official_id LIMIT @take) s ON TRUE
         WHERE i.issue_id = @issue
         ORDER BY s.sponsored DESC, s.official_id
        """;

    /// <summary>Reads the counts. Null means that the active release holds no such issue.</summary>
    public async Task<IssueCounts?> FindAsync(string issueId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("issue", issueId);
        command.Parameters.AddWithValue("kinds", SubstantiveKinds);
        command.Parameters.AddWithValue("take", MaximumSponsors);
        var sponsors = new List<(string OfficialId, int Bills)>();
        (int Release, int Votes, int Substantive, int Bills)? counts = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts ??= (reader.GetInt32(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2), (int)reader.GetInt64(3));
            if (!reader.IsDBNull(4)) sponsors.Add((reader.GetString(4), (int)reader.GetInt64(5)));
        }
        return counts is { } found
            ? new(found.Release, issueId, found.Votes, found.Substantive, found.Bills, [.. sponsors.Select(sponsor => new IssueSponsor(sponsor.OfficialId, sponsor.Bills, found.Bills))])
            : null;
    }
}
