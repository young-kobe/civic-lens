using System.Data;
using CivicLens.PublicApi.Http;
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
    // One row for each statement that a relationship links to the vote, with the approver of its link. The window count is the number of linked statements before the limit.
    // OFFSET 0 keeps the correlated lookups through the security barrier views, as on the official page.
    private const string StatementsSql = """
        SELECT s.record_id, s.statement_date, s.headline, s.label, s.text_origin, l.approver, count(*) OVER (),
               ARRAY(SELECT p.official_id FROM public_api.statement_speakers p
                      WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number ORDER BY p."position" OFFSET 0),
               ARRAY(SELECT o.name FROM public_api.statement_speakers p JOIN public_api.officials o ON o.official_id = p.official_id
                      WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number ORDER BY p."position" OFFSET 0)
          FROM (SELECT DISTINCT ON (k.related_record_id) k.related_record_id, k.approver
                  FROM public_api.record_links k
                 WHERE k.kind = 'vote' AND k.record_id = @id AND k.related_kind = 'statement'
                 ORDER BY k.related_record_id, k.link_record_id OFFSET 0) l
          JOIN public_api.statements s ON s.record_id = l.related_record_id
         ORDER BY s.statement_date DESC NULLS LAST, s.record_id
         LIMIT @take
        """;

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
            var bill = new NpgsqlBatchCommand("SELECT b.bill_title, b.bill_line, t.latest_action_text, t.latest_action_date, t.read_at_utc_ticks FROM public_api.bills b LEFT JOIN public_api.bill_status t ON t.record_id = b.record_id AND t.revision_number = b.revision_number WHERE b.record_id = @id");
            bill.Parameters.AddWithValue("id", billRecordId);
            batch.BatchCommands.Add(bill);
        }
        if (detail.IssueIds.Length > 0)
        {
            var issues = new NpgsqlBatchCommand("SELECT i.issue_id, i.name FROM public_api.release_issues i WHERE i.issue_id = ANY(@ids) ORDER BY i.name, i.issue_id");
            issues.Parameters.AddWithValue("ids", detail.IssueIds);
            batch.BatchCommands.Add(issues);
        }
        var linked = new NpgsqlBatchCommand(StatementsSql);
        linked.Parameters.AddWithValue("id", detail.Vote.RecordId);
        linked.Parameters.AddWithValue("take", VotePageView.StatementLimit);
        batch.BatchCommands.Add(linked);
        await using var reader = await batch.ExecuteReaderAsync(cancellationToken);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken)) names[reader.GetString(0)] = reader.GetString(1);
        VoteBillData? billData = null;
        if (billRecordId is not null)
        {
            await reader.NextResultAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken)) billData = ReadBill(reader, billRecordId, detail.Vote.BillType);
        }
        var issueData = new List<VoteIssueData>();
        if (detail.IssueIds.Length > 0)
        {
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) issueData.Add(new(reader.GetString(0), reader.GetString(1)));
        }
        await reader.NextResultAsync(cancellationToken);
        var statements = new List<VoteStatementData>();
        var total = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            total = (int)reader.GetInt64(6);
            statements.Add(ReadStatement(reader));
        }
        return new(names, billData, [.. issueData], [.. statements], total);
    }

    // Columns 2 to 4 hold the latest action. The view holds no row for a bill with no latest action. A text that no rule matches has no label and no law number.
    private static VoteBillData ReadBill(NpgsqlDataReader reader, string recordId, string? billType)
    {
        var bill = new VoteBillData(recordId, reader.GetString(0), ViewColumns.Text(reader, 1));
        if (ViewColumns.Text(reader, 2) is not { } action || billType is null) return bill;
        return bill with
        {
            StatusLabel = BillStatusMeaning.Label(action, billType),
            PublicLaw = BillStatusMeaning.PublicLawNumberOf(action),
            StatusDate = ViewColumns.Date(reader, 3),
            StatusReadAtUtc = new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero)
        };
    }

    private static VoteStatementData ReadStatement(NpgsqlDataReader reader)
    {
        var ids = reader.GetFieldValue<string[]>(7);
        var names = reader.GetFieldValue<string[]>(8);
        return new(reader.GetString(0), ViewColumns.Date(reader, 1), reader.GetString(2), ViewColumns.Text(reader, 3),
            ViewColumns.Enum<PublishedTextOrigin>(reader, 4), ViewColumns.NullableEnum<PublishedApprover>(reader, 5),
            [.. ids.Zip(names, (id, name) => new VoteStatementSpeaker(id, name))]);
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
            names.Bill, names.Issues, vote.BillType, vote.BillNumber)
        {
            Statements = names.Statements,
            StatementTotal = names.StatementTotal
        };
    }

    private sealed record PageNames(Dictionary<string, string> Officials, VoteBillData? Bill, VoteIssueData[] Issues, VoteStatementData[] Statements, int StatementTotal);
}
