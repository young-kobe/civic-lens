using System.Data;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using Npgsql;

namespace CivicLens.PublicApi.Votes;

/// <summary>
/// Reads everything that the server-rendered vote page shows: the vote detail, all tracked members, and the names of the officials, the bill, and the issues.
/// The detail and the member list are the existing vote reads. Each read answers with its own release, so a page is built only when every release matches.
/// </summary>
public sealed class VotePageReader(VoteStore votes, NpgsqlDataSource views)
{
    private const int Attempts = 2;

    /// <summary>Reads the page of one vote. Null means the active release holds no such vote. A release that changes during the reads is read again, and a second change throws.</summary>
    public async Task<VotePageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (await votes.FindAsync(recordId, cancellationToken) is not { } detail) return null;
            var request = new VoteMemberRequest { Limit = PublishedVote.MaximumMembers };
            if (await votes.ListMembersAsync(recordId, request, cancellationToken) is not { } members) return null;
            if (members.Release != detail.Release) continue;
            var names = await ReadNamesAsync(detail, members.Members, cancellationToken);
            if (names is not null) return Map(detail, members.Members, names);
        }
        throw new InvalidOperationException("The active release changed while the vote page was read.");
    }

    private async Task<PageNames?> ReadNamesAsync(VoteDetail detail, VoteMember[] members, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await using (var release = new NpgsqlCommand("SELECT release_number FROM public_api.active_release LIMIT 1", connection, transaction))
            if (await release.ExecuteScalarAsync(cancellationToken) is not int active || active != detail.Release) return null;
        return new(await ReadOfficialsAsync(connection, transaction, members, cancellationToken),
            await ReadBillAsync(connection, transaction, detail.Vote.BillRecordId, cancellationToken),
            await ReadIssuesAsync(connection, transaction, detail.IssueIds, cancellationToken));
    }

    private static async Task<Dictionary<string, string>> ReadOfficialsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        VoteMember[] members, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (members.Length == 0) return names;
        await using var command = new NpgsqlCommand("SELECT o.official_id, o.name FROM public_api.officials o WHERE o.official_id = ANY(@ids)", connection, transaction);
        command.Parameters.AddWithValue("ids", members.Select(member => member.OfficialId).Distinct().ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) names[reader.GetString(0)] = reader.GetString(1);
        return names;
    }

    private static async Task<VoteBillData?> ReadBillAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string? billRecordId,
        CancellationToken cancellationToken)
    {
        if (billRecordId is null) return null;
        await using var command = new NpgsqlCommand("SELECT b.bill_title, b.bill_line FROM public_api.bills b WHERE b.record_id = @id", connection, transaction);
        command.Parameters.AddWithValue("id", billRecordId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(billRecordId, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1)) : null;
    }

    private static async Task<VoteIssueData[]> ReadIssuesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string[] issueIds,
        CancellationToken cancellationToken)
    {
        if (issueIds.Length == 0) return [];
        await using var command = new NpgsqlCommand("SELECT i.issue_id, i.name FROM public_api.release_issues i WHERE i.issue_id = ANY(@ids) ORDER BY i.name, i.issue_id", connection, transaction);
        command.Parameters.AddWithValue("ids", issueIds);
        var issues = new List<VoteIssueData>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) issues.Add(new(reader.GetString(0), reader.GetString(1)));
        return [.. issues];
    }

    private static VotePageData Map(VoteDetail detail, VoteMember[] members, PageNames names)
    {
        var vote = detail.Vote;
        PublishedVoteTotals? totals = vote.Yea is { } yea && vote.Nay is { } nay && vote.Present is { } present && vote.NotVoting is { } notVoting
            ? new() { Yea = yea, Nay = nay, Present = present, NotVoting = notVoting }
            : null;
        var memberData = members.Select(member => new VoteMemberData(member.OfficialId,
            names.Officials.TryGetValue(member.OfficialId, out var name) ? name : throw new InvalidDataException("A member of a published vote has no official name."),
            member.Cast, member.Party, member.Citation)).ToArray();
        return new(detail.Release, vote.RecordId, vote.RevisionNumber, vote.Chamber, vote.Congress, vote.Session, vote.RollCall, vote.VotedOn,
            vote.Kind, detail.Source, vote.Headline, vote.Summary, vote.TextOrigin, vote.Label, vote.Result, vote.Alignment, totals,
            detail.PartyTallies, detail.VotedOnCitation, detail.KindCitation, detail.Facts, detail.Sources, memberData,
            names.Bill, names.Issues, vote.BillType, vote.BillNumber);
    }

    private sealed record PageNames(Dictionary<string, string> Officials, VoteBillData? Bill, VoteIssueData[] Issues);
}
