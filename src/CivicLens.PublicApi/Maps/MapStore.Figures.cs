using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Officials;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Maps;

public sealed partial class MapStore
{
    private static readonly string[] SubstantiveKinds = PublishedVoteKindExtensions.SubstantiveNames();

    // The agreement view retains active vote bindings without a typed revision as a null Congress group.
    private const string VoteCoverage = """
        NOT EXISTS (
            SELECT 1 FROM public_api.party_agreement g
             WHERE g.release_number = @release AND g.congress IS NULL
        )
        """;

    private const string AttendanceSql = $"""
        WITH figures AS MATERIALIZED (
            SELECT a.official_id, a.chamber, a.congress, a.held, a.missed, a.first_voted_on, a.last_voted_on
              FROM public_api.official_attendance a
             WHERE a.congress = @congress AND a.chamber = @chamber
               AND {VoteCoverage}
        )
        SELECT official_id, chamber, congress, held, missed, first_voted_on, last_voted_on
          FROM figures WHERE official_id = ANY(@ids)
        """;

    // The header is the completeness guarantee for the sparse member counts.
    private const string BillBasesSql = """
        SELECT m.official_id, m.sponsored
          FROM public_api.release_bill_counts c
          LEFT JOIN public_api.release_official_bill_counts m ON m.release_number = c.release_number
           AND m.congress = c.congress AND m.official_id = ANY(@ids)
         WHERE c.release_number = @release AND c.congress = @congress
        """;

    private const string VoteBasesSql = $"""
        SELECT m.official_id, count(*) FILTER (WHERE m.kind = ANY(@kinds) AND m.vote_cast <> 'Not Voting')
          FROM public_api.member_votes m
         WHERE m.congress = @congress AND m.official_id = ANY(@ids) AND {VoteCoverage}
         GROUP BY m.official_id
        """;

    private const string IssueCountsSql = """
        SELECT c.official_id,
               sum(c.records) FILTER (WHERE c.source = 'bill'),
               sum(c.records) FILTER (WHERE c.source = 'vote')
          FROM public_api.official_issue_counts c
         WHERE c.congress = @congress AND c.issue_id = @issue AND c.official_id = ANY(@ids)
           AND (c.source = 'bill' OR (c.source = 'vote' AND c.vote_kind = ANY(@kinds)))
         GROUP BY c.official_id
        """;

    private static async Task<MapMemberAnswer[]> ReadAttendanceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int release,
        int congress, PublishedChamber chamber, MapMemberAnswer[] members, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(AttendanceSql, connection, transaction);
        command.Parameters.AddWithValue("release", release);
        command.Parameters.AddWithValue("congress", congress);
        command.Parameters.AddWithValue("chamber", chamber.ToString());
        command.Parameters.AddWithValue("ids", members.Select(member => member.OfficialId).ToArray());

        var figures = new Dictionary<string, MissedVotesFigure>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            figures.Add(reader.GetString(0), OfficialAttendanceReader.ReadFigure(reader, 1));

        return [.. members.Select(member => member with { Attendance = figures.GetValueOrDefault(member.OfficialId) })];
    }

    private static async Task<MapMemberAnswer[]> ReadActivityAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int release,
        int congress, string issue, MapMemberAnswer[] members, CancellationToken cancellationToken)
    {
        var ids = members.Select(member => member.OfficialId).ToArray();
        await using var batch = new NpgsqlBatch(connection, transaction);
        var bills = AddMetricsCommand(batch, BillBasesSql, congress, ids);
        bills.Parameters.AddWithValue("release", release);

        var votes = AddMetricsCommand(batch, VoteBasesSql, congress, ids);
        votes.Parameters.AddWithValue("release", release);
        votes.Parameters.AddWithValue("kinds", SubstantiveKinds);

        var matching = AddMetricsCommand(batch, IssueCountsSql, congress, ids);
        matching.Parameters.AddWithValue("issue", issue);
        matching.Parameters.AddWithValue("kinds", SubstantiveKinds);

        await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
        var billBases = new Dictionary<string, int>(StringComparer.Ordinal);
        var hasBillSnapshot = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            hasBillSnapshot = true;
            if (!reader.IsDBNull(0)) billBases.Add(reader.GetString(0), reader.GetInt32(1));
        }

        await reader.NextResultAsync(cancellationToken);
        var voteBases = new Dictionary<string, int>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
            voteBases.Add(reader.GetString(0), ViewColumns.Number(reader, 1)!.Value);

        await reader.NextResultAsync(cancellationToken);
        var numerators = new Dictionary<string, (int? Bills, int? Votes)>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken))
            numerators.Add(reader.GetString(0), (ViewColumns.Number(reader, 1), ViewColumns.Number(reader, 2)));

        return [.. members.Select(member =>
        {
            var id = member.OfficialId;
            var (billMatches, voteMatches) = numerators.GetValueOrDefault(id);
            return member with
            {
                IssueActivity = new(
                    Counts(billMatches, hasBillSnapshot ? billBases.GetValueOrDefault(id) : null),
                    Counts(voteMatches, voteBases.TryGetValue(id, out var voteBase) ? voteBase : null))
            };
        })];
    }

    private static NpgsqlBatchCommand AddMetricsCommand(NpgsqlBatch batch, string sql, int congress, string[] ids)
    {
        var command = new NpgsqlBatchCommand(sql);
        command.Parameters.AddWithValue("congress", congress);
        command.Parameters.AddWithValue("ids", ids);
        batch.BatchCommands.Add(command);
        return command;
    }

    private static MapCounts? Counts(int? matches, int? basis)
    {
        var matching = matches ?? 0;
        return basis is { } total && matching >= 0 && matching <= total ? new(matching, total) : null;
    }
}
