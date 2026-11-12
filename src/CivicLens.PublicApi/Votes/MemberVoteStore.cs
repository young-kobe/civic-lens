using System.Data;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.Votes;

/// <summary>Reads the view <c>public_api.member_votes</c> for the votes of the asked officials and the issue filter, and <c>public_api.active_release</c> for the release. The kind rule is <see cref="PublishedVoteKindExtensions.SubstantiveNames"/>.</summary>
public sealed class MemberVoteStore(NpgsqlDataSource views)
{
    /// <summary>The most votes of one official in one answer. The list is a short recent view, not a history. The vote list route pages through all votes.</summary>
    public const int MaximumVotes = 10;
    /// <summary>The most shared votes selected for each chamber.</summary>
    public const int MaximumSharedVotesPerChamber = 10;

    // Each official gets a lateral read of its own, so the person index drives the plan. The issue filter reads the column issue_ids of the same view.
    // A filter on public_api.vote_issues made Postgres build the active records again for each row.
    private static string Sql(MemberVoteRequest request) => $"""
        SELECT a.release_number, o.official_id, v.record_id, v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind,
               v.bill_type, v.bill_number, v.headline, v.label, v.result, v.vote_cast, v.side, v.party_majority_cast
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
         CROSS JOIN unnest(@officials) WITH ORDINALITY AS o(official_id, ask_order)
          LEFT JOIN LATERAL (
               SELECT v.* FROM public_api.member_votes v
                WHERE v.official_id = o.official_id AND v.kind = ANY(@kinds){IssueFilter(request)}
                ORDER BY {VoteStore.Order} LIMIT @take) v
            ON TRUE
         ORDER BY o.ask_order, {VoteStore.Order}
        """;

    private static string IssueFilter(MemberVoteRequest request) => request.Issue is null ? "" : """

                  AND @issue = ANY(v.issue_ids)
        """;

    private static string SharedSql(MemberVoteRequest request) => $"""
        SELECT a.release_number, selected.record_id, selected.voted_on, selected.chamber, selected.congress, selected.session_number,
               selected.roll_call_number, selected.kind, selected.bill_type, selected.bill_number, selected.headline, selected.label, selected.result
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT chosen.record_id, chosen.voted_on, chosen.chamber, chosen.congress, chosen.session_number, chosen.roll_call_number,
                      chosen.kind, chosen.bill_type, chosen.bill_number, chosen.headline, chosen.label, chosen.result
                 FROM unnest(@chambers) AS chambers(chamber)
                 CROSS JOIN LATERAL ({SharedSelection(request)}) chosen
                ) selected ON TRUE
         ORDER BY {VoteStore.Order.Replace("v.", "selected.", StringComparison.Ordinal)}
        """;

    private static string SharedSelection(MemberVoteRequest request) => request.Issue is null ? $"""
        SELECT v.record_id, v.voted_on, v.chamber, v.congress, v.session_number, v.roll_call_number, v.kind, v.bill_type, v.bill_number,
               v.headline, v.label, v.result
          FROM public_api.votes v
         WHERE v.chamber = chambers.chamber AND v.kind = ANY(@kinds)
         ORDER BY {VoteStore.Order} LIMIT @take
        """ : $"""
        SELECT v.record_id, v.voted_on, v.chamber, v.congress, v.session_number, v.roll_call_number, v.kind, v.bill_type, v.bill_number,
               v.headline, v.label, v.result
          FROM (SELECT DISTINCT record_id, revision_number FROM public_api.vote_issues WHERE issue_id = @issue) issue_keys
         CROSS JOIN LATERAL (
              SELECT v.record_id, v.voted_on, v.chamber, v.congress, v.session_number, v.roll_call_number, v.kind, v.bill_type, v.bill_number,
                     v.headline, v.label, v.result
                FROM public_api.votes v
               WHERE v.record_id = issue_keys.record_id AND v.revision_number = issue_keys.revision_number
                 AND v.chamber = chambers.chamber AND v.kind = ANY(@kinds)
               OFFSET 0) v
         ORDER BY {VoteStore.Order} LIMIT @take
        """;

    private const string SharedCastsSql = $"""
        SELECT o.official_id, v.record_id, v.vote_cast, v.side, v.party_majority_cast
          FROM unnest(@officials) WITH ORDINALITY AS o(official_id, ask_order)
          JOIN public_api.member_votes v ON v.official_id = o.official_id AND v.record_id = ANY(@record_ids)
         ORDER BY o.ask_order
        """;

    /// <summary>Reads the newest votes. Null means that no release is active. One statement reads the release and the votes, so both come from one release.</summary>
    public async Task<MemberVoteAnswer?> FindAsync(MemberVoteRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql(request));
        return await ReadAsync(command, request, MaximumVotes, cancellationToken);
    }

    /// <summary>Reads the newest votes inside the caller's snapshot. <paramref name="take"/> is the most votes of each official, at most <see cref="MaximumVotes"/>, so a page reads only the votes it shows.</summary>
    internal static async Task<MemberVoteAnswer?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, MemberVoteRequest request, int take,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, MaximumVotes);
        await using var command = new NpgsqlCommand(Sql(request), connection, transaction);
        return await ReadAsync(command, request, take, cancellationToken);
    }

    private static async Task<MemberVoteAnswer?> ReadAsync(NpgsqlCommand command, MemberVoteRequest request, int take, CancellationToken cancellationToken)
    {
        command.Parameters.AddWithValue("officials", request.Officials);
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        command.Parameters.AddWithValue("take", take);
        if (request.Issue is not null) command.Parameters.AddWithValue("issue", request.Issue);
        var votes = request.Officials.ToDictionary(official => official, _ => new List<MemberVote>(), StringComparer.Ordinal);
        int? release = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            if (!reader.IsDBNull(2)) votes[reader.GetString(1)].Add(ReadVote(reader));
        }
        return release is { } active ? new(active, [.. request.Officials.Select(official => new OfficialVotes(official, [.. votes[official]]))]) : null;
    }

    private const string CastsOfVotesSql = """
        SELECT a.release_number, v.official_id, v.record_id, v.vote_cast, v.side, v.party_majority_cast
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN public_api.member_votes v ON v.official_id = ANY(@officials) AND v.record_id = ANY(@record_ids)
        """;

    /// <summary>Reads the casts of the asked officials on the asked votes, grouped by vote record ID. Null means that no release is active. One statement reads the release and the casts.</summary>
    public async Task<MemberCastAnswer?> FindCastsAsync(string[] officials, string[] recordIds, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(CastsOfVotesSql);
        command.Parameters.AddWithValue("officials", officials);
        command.Parameters.AddWithValue("record_ids", recordIds);
        var casts = new Dictionary<string, List<SharedMemberVoteCast>>(StringComparer.Ordinal);
        int? release = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            if (reader.IsDBNull(1)) continue;
            var cast = reader.GetString(3);
            if (!casts.TryGetValue(reader.GetString(2), out var list)) casts[reader.GetString(2)] = list = [];
            list.Add(new(reader.GetString(1), cast, PlainVoteWords.Cast(cast), ViewColumns.Text(reader, 4), ViewColumns.Text(reader, 5)));
        }
        return release is { } active ? new(active, casts.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal)) : null;
    }

    /// <summary>Reads a bounded shared set and every requested official's recorded cast in one release snapshot.</summary>
    public async Task<SharedMemberVoteAnswer?> FindSharedAsync(MemberVoteRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        return await ReadSharedAsync(connection, transaction, request, cancellationToken);
    }

    internal static async Task<SharedMemberVoteAnswer?> ReadSharedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        MemberVoteRequest request, CancellationToken cancellationToken)
    {
        var (release, selected) = await ReadSharedVotesAsync(connection, transaction, request, cancellationToken);
        if (release is not { } active) return null;
        var casts = selected.ToDictionary(vote => vote.RecordId, _ => new List<SharedMemberVoteCast>(), StringComparer.Ordinal);
        if (selected.Count > 0) await ReadSharedCastsAsync(connection, transaction, request.Officials, [.. casts.Keys], casts, cancellationToken);
        return new(active, request.Officials,
            [.. selected.Select(vote => vote with { Casts = [.. casts[vote.RecordId]] })]);
    }

    private static async Task<(int? Release, List<SharedMemberVote> Votes)> ReadSharedVotesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, MemberVoteRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SharedSql(request), connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("chambers", Enum.GetNames<PublishedChamber>());
        command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        command.Parameters.AddWithValue("take", MaximumSharedVotesPerChamber);
        if (request.Issue is not null) command.Parameters.AddWithValue("issue", request.Issue);
        int? release = null;
        var votes = new List<SharedMemberVote>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) votes.Add(ReadSharedVote(reader));
        }
        return (release, votes);
    }

    private static async Task ReadSharedCastsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string[] officials, string[] recordIds,
        Dictionary<string, List<SharedMemberVoteCast>> casts, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SharedCastsSql, connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("officials", officials);
        command.Parameters.AddWithValue("record_ids", recordIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var cast = reader.GetString(2);
            casts[reader.GetString(1)].Add(new(reader.GetString(0), cast, PlainVoteWords.Cast(cast), ViewColumns.Text(reader, 3), ViewColumns.Text(reader, 4)));
        }
    }

    private static SharedMemberVote ReadSharedVote(NpgsqlDataReader reader)
    {
        var kind = ViewColumns.Enum<PublishedVoteKind>(reader, 7);
        var result = reader.GetString(12);
        return new(reader.GetString(1), reader.GetFieldValue<DateOnly>(reader.GetOrdinal("voted_on")),
            ViewColumns.Enum<PublishedChamber>(reader, 3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), kind,
            PlainVoteWords.Kind(kind), ViewColumns.Text(reader, 8), ViewColumns.Number(reader, 9), reader.GetString(10), ViewColumns.Text(reader, 11),
            result, PlainVoteWords.Result(result), []);
    }

    private static MemberVote ReadVote(NpgsqlDataReader reader)
    {
        var kind = ViewColumns.Enum<PublishedVoteKind>(reader, 8);
        var result = reader.GetString(13);
        var cast = reader.GetString(14);
        return new(reader.GetString(2), reader.GetFieldValue<DateOnly>(7), ViewColumns.Enum<PublishedChamber>(reader, 3), reader.GetInt32(4), reader.GetInt32(5),
            reader.GetInt32(6), kind, PlainVoteWords.Kind(kind), ViewColumns.Text(reader, 9), ViewColumns.Number(reader, 10), reader.GetString(11),
            ViewColumns.Text(reader, 12), result, PlainVoteWords.Result(result), cast, PlainVoteWords.Cast(cast), ViewColumns.Text(reader, 15), ViewColumns.Text(reader, 16));
    }
}
