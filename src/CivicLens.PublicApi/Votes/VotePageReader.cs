using System.Data;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using Npgsql;

namespace CivicLens.PublicApi.Votes;

/// <summary>
/// Reads everything that the server-rendered vote page shows: the vote detail, all tracked members, and the names of the officials, the bill, and the issues.
/// One read-only REPEATABLE READ transaction on one connection holds every read, so the page comes from one snapshot and one release.
/// The detail already reads the source texts, so the member read does not read them again. The three name reads go out as one batch.
/// </summary>
public sealed class VotePageReader(VoteStore votes, NpgsqlDataSource views)
{
    /// <summary>Reads the page of one vote. Null means the active release holds no such vote.</summary>
    public async Task<VotePageData?> ReadAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await using (var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
            await readOnly.ExecuteNonQueryAsync(cancellationToken);
        if (await votes.FindAsync(transaction, recordId, cancellationToken) is not { } detail) return null;
        var request = new VoteMemberRequest { Limit = PublishedVote.MaximumMembers };
        if (await votes.ListMembersAsync(transaction, recordId, request, detail.Sources, cancellationToken) is not { } members) return null;
        var names = await ReadNamesAsync(connection, transaction, detail, members.Members, cancellationToken);
        return Map(detail, members.Members, names);
    }

    private static async Task<PageNames> ReadNamesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, VoteDetail detail,
        VoteMember[] members, CancellationToken cancellationToken)
    {
        await using var batch = new NpgsqlBatch(connection, transaction);
        var officials = new NpgsqlBatchCommand("SELECT o.official_id, o.name FROM public_api.officials o WHERE o.official_id = ANY(@ids)");
        officials.Parameters.AddWithValue("ids", members.Select(member => member.OfficialId).Distinct().ToArray());
        batch.BatchCommands.Add(officials);
        var billRecordId = detail.Vote.BillRecordId;
        if (billRecordId is not null)
        {
            var bill = new NpgsqlBatchCommand("SELECT b.bill_title, b.bill_line FROM public_api.bills b WHERE b.record_id = @id");
            bill.Parameters.AddWithValue("id", billRecordId);
            batch.BatchCommands.Add(bill);
        }
        if (detail.IssueIds.Length > 0)
        {
            var issues = new NpgsqlBatchCommand("SELECT i.issue_id, i.name FROM public_api.release_issues i WHERE i.issue_id = ANY(@ids) ORDER BY i.name, i.issue_id");
            issues.Parameters.AddWithValue("ids", detail.IssueIds);
            batch.BatchCommands.Add(issues);
        }
        await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken)) names[reader.GetString(0)] = reader.GetString(1);
        VoteBillData? billData = null;
        if (billRecordId is not null)
        {
            await reader.NextResultAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken)) billData = new(billRecordId, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
        }
        var issueData = new List<VoteIssueData>();
        if (detail.IssueIds.Length > 0)
        {
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) issueData.Add(new(reader.GetString(0), reader.GetString(1)));
        }
        return new(names, billData, [.. issueData]);
    }

    private static VotePageData Map(VoteDetail detail, VoteMember[] members, PageNames names)
    {
        var vote = detail.Vote;
        PublishedVoteTotals? totals = vote.Yea is { } yea && vote.Nay is { } nay && vote.Present is { } present && vote.NotVoting is { } notVoting
            ? new() { Yea = yea, Nay = nay, Present = present, NotVoting = notVoting }
            : null;
        var memberData = members.Select(member => new VoteMemberData(member.OfficialId,
            names.Officials.TryGetValue(member.OfficialId, out var name) ? name : throw new InvalidDataException("A member of a published vote has no official name."),
            member.Cast, member.Party, member.Citation, member.State, member.District)).ToArray();
        return new(detail.Release, vote.RecordId, vote.RevisionNumber, vote.Chamber, vote.Congress, vote.Session, vote.RollCall, vote.VotedOn,
            vote.Kind, detail.Source, vote.Headline, vote.Summary, vote.TextOrigin, vote.Label, vote.Result, vote.Alignment, totals,
            detail.PartyTallies, detail.VotedOnCitation, detail.KindCitation, detail.Facts, detail.Sources, memberData,
            names.Bill, names.Issues, vote.BillType, vote.BillNumber);
    }

    private sealed record PageNames(Dictionary<string, string> Officials, VoteBillData? Bill, VoteIssueData[] Issues);
}
