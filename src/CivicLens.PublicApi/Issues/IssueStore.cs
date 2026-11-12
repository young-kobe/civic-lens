using System.Data;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Votes;
using Npgsql;

namespace CivicLens.PublicApi.Issues;

/// <summary>Reads the views <c>public_api.release_issues</c>, <c>active_release</c>, <c>issue_votes</c>, <c>bill_issues</c>, and <c>issue_sponsors</c> for one issue.
/// One snapshot serves both statements, so every count comes from one release, as in <c>ZipDistrictStore</c>.
/// The kind rule is <see cref="PublishedVoteKindExtensions.SubstantiveNames"/>. A sponsor is a sponsorship with the role <c>Sponsor</c>, as in <c>bills.sponsor_count</c>.
/// The current Congress is the Congress of the publication day of the release, from <see cref="CongressCalendar"/>.
/// The page answer adds the issue name and definition and the newest votes and bills of the issue to the same snapshot. Its counts and lists cover the Congress of the release (<c>release_issues.count_congress</c>), and the votes and bills of each count and its list come from one shared set.
/// The line-up reads <c>release_issue_party_lineups</c> in the same snapshot, and its base is the Home tile count <c>release_issues.bills</c>.</summary>
public sealed class IssueStore(NpgsqlDataSource views)
{
    /// <summary>The most officials in one answer. The issue page shows 5.</summary>
    public const int MaximumSponsors = 5;

    /// <summary>The most votes and the most bills on the issue page.</summary>
    public const int PageVotes = 10;
    public const int PageBills = 5;

    private const string HeaderSql = """
        SELECT i.release_number, a.published_at_utc_ticks, i.count_congress, i.bills, i.name, i.definition
          FROM public_api.release_issues i
          LEFT JOIN (SELECT published_at_utc_ticks FROM public_api.active_release LIMIT 1) a ON TRUE
         WHERE i.issue_id = @issue
        """;

    private const string TotalsSql = """
        SELECT (SELECT count(*) FROM public_api.issue_votes x WHERE x.issue_id = @issue AND x.kind = ANY(@kinds)),
               (SELECT count(*) FROM public_api.bill_issues x WHERE x.issue_id = @issue)
        """;

    // The vote set of the page: the substantive votes of the issue in the Congress of the release. A null Congress means that the release stores none, so the set is not narrowed.
    private const string PageVoteSet = """
          FROM (SELECT DISTINCT record_id, revision_number FROM public_api.vote_issues WHERE issue_id = @issue) k
         CROSS JOIN LATERAL (
              SELECT v.* FROM public_api.votes v
               WHERE v.record_id = k.record_id AND v.revision_number = k.revision_number AND v.kind = ANY(@kinds)
                 AND (@congress::integer IS NULL OR v.congress = @congress)
               OFFSET 0) v
        """;

    private const string PageBillSet = """
          FROM public_api.bills b
         WHERE EXISTS (SELECT 1 FROM public_api.bill_issues i
                        WHERE i.record_id = b.record_id AND i.revision_number = b.revision_number AND i.issue_id = @issue)
           AND (@congress::integer IS NULL OR b.congress = @congress)
        """;

    private const string PageTotalsSql = $"""
        SELECT (SELECT count(*) {PageVoteSet}),
               (SELECT count(*) {PageVoteSet} WHERE v.alignment = 'Split'),
               (SELECT count(*) {PageBillSet})
        """;

    // The views hold the active release only. A term keeps its stored position within its list.
    private const string TermsSql = """
        SELECT 'includes' AS side, "position", text FROM public_api.release_issue_includes WHERE issue_id = @issue
        UNION ALL
        SELECT 'excludes', "position", text FROM public_api.release_issue_excludes WHERE issue_id = @issue
        ORDER BY side, "position"
        """;

    private const string LineUpSql = """
        SELECT unknown_bills, party, bills FROM public_api.release_issue_party_lineups WHERE issue_id = @issue ORDER BY bills DESC NULLS LAST, party
        """;

    private static readonly string VotesSql = $"""
        SELECT v.record_id, v.voted_on, v.chamber, v.headline, v.label, v.result, v.yea, v.nay
          {PageVoteSet}
         ORDER BY {VoteStore.Order} LIMIT @take
        """;

    private const string BillsSql = $"""
        SELECT b.record_id, b.headline, b.label, b.event_date, b.bill_line,
               (SELECT o.name FROM public_api.bill_sponsorships s
                  JOIN public_api.officials o ON o.official_id = s.official_id
                 WHERE s.record_id = b.record_id AND s.revision_number = b.revision_number AND s.role = 'Sponsor'
                 ORDER BY s."position" LIMIT 1)
          {PageBillSet}
         ORDER BY b.event_date DESC, b.record_id LIMIT @take
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
        if (await ReadHeaderAsync(connection, issueId, cancellationToken) is not { } header) return null;
        var (substantiveVotes, bills) = await ReadTotalsAsync(connection, issueId, cancellationToken);
        var sponsors = header.PublishedOn is { } day ? await ReadSponsorsAsync(connection, issueId, day, cancellationToken) : [];
        var lineUp = await ReadLineUpAsync(connection, issueId, header, cancellationToken);
        return new(header.Release, issueId, substantiveVotes, bills, [.. sponsors.Select(sponsor => new IssueSponsor(sponsor.OfficialId, sponsor.Bills, bills))], lineUp);
    }

    /// <summary>Reads the page answer of one issue in one release snapshot: the counts, the line-up, the newest substantive votes, and the newest bills.
    /// Null means that the active release holds no such issue. The page needs no sponsors.</summary>
    public async Task<IssuePageAnswer?> FindPageAsync(string issueId, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        if (await ReadHeaderAsync(connection, issueId, cancellationToken) is not { } header) return null;
        var lineUp = await ReadLineUpAsync(connection, issueId, header, cancellationToken);
        var totals = await ReadPageTotalsAsync(connection, issueId, header.HomeCongress, cancellationToken);
        var (includes, excludes) = await ReadTermsAsync(connection, issueId, cancellationToken);
        var votes = await ReadVotesAsync(connection, issueId, header.HomeCongress, cancellationToken);
        var bills = await ReadBillsAsync(connection, issueId, header.HomeCongress, cancellationToken);
        return new(header.Release, issueId, header.Name, header.Definition, includes, excludes, header.HomeCongress, totals.Votes, totals.SplitVotes, totals.Bills, lineUp, votes, bills);
    }

    private static async Task<IssueVote[]> ReadVotesAsync(NpgsqlConnection connection, string issueId, int? congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(VotesSql, connection);
        command.Parameters.AddWithValue("issue", issueId);
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        AddCongress(command, congress);
        command.Parameters.AddWithValue("take", PageVotes);
        var votes = new List<IssueVote>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            votes.Add(new(reader.GetString(0), reader.GetFieldValue<DateOnly>(1), ViewColumns.Enum<PublishedChamber>(reader, 2), reader.GetString(3),
                ViewColumns.Text(reader, 4), PlainVoteWords.Result(reader.GetString(5)), ViewColumns.Number(reader, 6), ViewColumns.Number(reader, 7)));
        return [.. votes];
    }

    private static async Task<IssueBill[]> ReadBillsAsync(NpgsqlConnection connection, string issueId, int? congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(BillsSql, connection);
        command.Parameters.AddWithValue("issue", issueId);
        AddCongress(command, congress);
        command.Parameters.AddWithValue("take", PageBills);
        var bills = new List<IssueBill>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            bills.Add(new(reader.GetString(0), reader.GetString(1), ViewColumns.Text(reader, 2), reader.GetFieldValue<DateOnly>(3), ViewColumns.Text(reader, 4), ViewColumns.Text(reader, 5)));
        return [.. bills];
    }

    private static async Task<(string[] Includes, string[] Excludes)> ReadTermsAsync(NpgsqlConnection connection, string issueId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(TermsSql, connection);
        command.Parameters.AddWithValue("issue", issueId);
        var includes = new List<string>();
        var excludes = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) (reader.GetString(0) == "includes" ? includes : excludes).Add(reader.GetString(2));
        return ([.. includes], [.. excludes]);
    }

    private sealed record Header(int Release, DateOnly? PublishedOn, int? HomeCongress, int? HomeBills, string Name, string? Definition);

    private sealed record PageTotals(int Votes, int SplitVotes, int Bills);

    private static void AddCongress(NpgsqlCommand command, int? congress) =>
        command.Parameters.Add(new NpgsqlParameter("congress", NpgsqlTypes.NpgsqlDbType.Integer) { Value = (object?)congress ?? DBNull.Value });

    private static async Task<Header?> ReadHeaderAsync(NpgsqlConnection connection, string issueId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(HeaderSql, connection);
        command.Parameters.AddWithValue("issue", issueId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        DateOnly? publishedOn = reader.IsDBNull(1) ? null : DateOnly.FromDateTime(new DateTime(reader.GetInt64(1), DateTimeKind.Utc));
        return new(reader.GetInt32(0), publishedOn, reader.IsDBNull(2) ? null : reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetInt32(3),
            reader.GetString(4), ViewColumns.Text(reader, 5));
    }

    private static async Task<(int SubstantiveVotes, int Bills)> ReadTotalsAsync(NpgsqlConnection connection, string issueId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(TotalsSql, connection);
        command.Parameters.AddWithValue("issue", issueId);
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ((int)reader.GetInt64(0), (int)reader.GetInt64(1));
    }

    private static async Task<PageTotals> ReadPageTotalsAsync(NpgsqlConnection connection, string issueId, int? congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PageTotalsSql, connection);
        command.Parameters.AddWithValue("issue", issueId);
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        AddCongress(command, congress);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new((int)reader.GetInt64(0), (int)reader.GetInt64(1), (int)reader.GetInt64(2));
    }

    /// <summary>Null means that the release has no line-up row for the issue. Rows that do not add up to the Home tile count fail the request, so a wrong split never shows.</summary>
    private static async Task<IssuePartyLineUp?> ReadLineUpAsync(NpgsqlConnection connection, string issueId, Header counts, CancellationToken cancellationToken)
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
