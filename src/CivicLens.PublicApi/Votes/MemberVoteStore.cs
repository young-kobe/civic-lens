using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.Votes;

/// <summary>Reads the view <c>public_api.member_votes</c> for the votes of one official, and <c>public_api.active_records</c> for the release. The kind rule is <see cref="PublishedVoteKindExtensions.IsSubstantive"/>.</summary>
public sealed class MemberVoteStore(NpgsqlDataSource views)
{
    /// <summary>The most votes in one answer. The list is a short recent view, not a history. The vote list route pages through all votes.</summary>
    public const int MaximumVotes = 10;

    private static readonly string[] SubstantiveKinds = [.. Enum.GetValues<PublishedVoteKind>().Where(kind => kind.IsSubstantive()).Select(kind => kind.ToString())];

    private const string Sql = $"""
        SELECT a.release_number, v.record_id, v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind,
               v.bill_type, v.bill_number, v.headline, v.label, v.result, v.vote_cast
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT v.* FROM public_api.member_votes v
                WHERE v.official_id = @official AND v.kind = ANY(@kinds)
                ORDER BY {VoteStore.Order} LIMIT @take) v
            ON TRUE
         ORDER BY {VoteStore.Order}
        """;

    /// <summary>Reads the newest votes. Null means that no release is active. One statement reads the release and the votes, so both come from one release.</summary>
    public async Task<MemberVoteAnswer?> FindAsync(MemberVoteRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("official", request.Official);
        command.Parameters.AddWithValue("kinds", SubstantiveKinds);
        command.Parameters.AddWithValue("take", MaximumVotes);
        var votes = new List<MemberVote>();
        int? release = null;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) votes.Add(ReadVote(reader));
        }
        return release is { } active ? new(active, request.Official, [.. votes]) : null;
    }

    private static MemberVote ReadVote(NpgsqlDataReader reader)
    {
        var kind = ViewColumns.Enum<PublishedVoteKind>(reader, 7);
        var result = reader.GetString(12);
        var cast = reader.GetString(13);
        return new(reader.GetString(1), reader.GetFieldValue<DateOnly>(6), ViewColumns.Enum<PublishedChamber>(reader, 2), reader.GetInt32(3), reader.GetInt32(4),
            reader.GetInt32(5), kind, PlainVoteWords.Kind(kind), ViewColumns.Text(reader, 8), ViewColumns.Number(reader, 9), reader.GetString(10),
            ViewColumns.Text(reader, 11), result, PlainVoteWords.Result(result), cast, PlainVoteWords.Cast(cast));
    }
}
