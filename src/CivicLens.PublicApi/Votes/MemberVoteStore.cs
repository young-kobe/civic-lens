using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.Votes;

/// <summary>Reads the view <c>public_api.member_votes</c> for the votes of the asked officials and the issue filter, and <c>public_api.active_records</c> for the release. The kind rule is <see cref="PublishedVoteKindExtensions.IsSubstantive"/>.</summary>
public sealed class MemberVoteStore(NpgsqlDataSource views)
{
    /// <summary>The most votes of one official in one answer. The list is a short recent view, not a history. The vote list route pages through all votes.</summary>
    public const int MaximumVotes = 10;

    private static readonly string[] SubstantiveKinds = [.. Enum.GetValues<PublishedVoteKind>().Where(kind => kind.IsSubstantive()).Select(kind => kind.ToString())];

    // Each official gets a lateral read of its own, so the person index drives the plan. The issue filter reads the column issue_ids of the same view.
    // A filter on public_api.vote_issues made Postgres build the active records again for each row.
    private static string Sql(MemberVoteRequest request) => $"""
        SELECT a.release_number, o.official_id, v.record_id, v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind,
               v.bill_type, v.bill_number, v.headline, v.label, v.result, v.vote_cast
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
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

    /// <summary>Reads the newest votes. Null means that no release is active. One statement reads the release and the votes, so both come from one release.</summary>
    public async Task<MemberVoteAnswer?> FindAsync(MemberVoteRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql(request));
        command.Parameters.AddWithValue("officials", request.Officials);
        command.Parameters.AddWithValue("kinds", SubstantiveKinds);
        command.Parameters.AddWithValue("take", MaximumVotes);
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

    private static MemberVote ReadVote(NpgsqlDataReader reader)
    {
        var kind = ViewColumns.Enum<PublishedVoteKind>(reader, 8);
        var result = reader.GetString(13);
        var cast = reader.GetString(14);
        return new(reader.GetString(2), reader.GetFieldValue<DateOnly>(7), ViewColumns.Enum<PublishedChamber>(reader, 3), reader.GetInt32(4), reader.GetInt32(5),
            reader.GetInt32(6), kind, PlainVoteWords.Kind(kind), ViewColumns.Text(reader, 9), ViewColumns.Number(reader, 10), reader.GetString(11),
            ViewColumns.Text(reader, 12), result, PlainVoteWords.Result(result), cast, PlainVoteWords.Cast(cast));
    }
}
