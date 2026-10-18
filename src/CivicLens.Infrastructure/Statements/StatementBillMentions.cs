using System.Globalization;
using CivicLens.Application.Actions;
using CivicLens.Application.Relationships;
using CivicLens.Application.Votes;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Collection.HouseVotes;
using CivicLens.Infrastructure.Votes;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

// Statement bill numbers come from the BillNumbers that each drafted revision stores, and from the article text of a new candidate.
internal static class StatementBillMentions
{
    private const string MentionsEndingInNumber = """
        SELECT m AS "Citation", r.revision_json::jsonb ->> 'statementDate' AS "StatementDate", c.created_at_utc_ticks AS "CapturedAtUtcTicks"
        FROM statement_drafts d
        JOIN statement_revisions r ON r.candidate_id = d.candidate_id AND r.revision_number = d.current_revision_number
        JOIN statement_candidates c ON c.candidate_id = d.candidate_id
        CROSS JOIN LATERAL jsonb_array_elements_text(r.revision_json::jsonb -> 'billNumbers') m
        WHERE m LIKE {0}
        """;

    public static async Task<int> PriorityOfBillAsync(CollectionAttemptDbContext db, int congress, string? billType, int? billNumber,
        bool hasTrackedIssue, CancellationToken cancellationToken) =>
        ReviewPriority.Of(billType is not null && await IsNamedAsync(db, congress, billType, billNumber!.Value, cancellationToken), hasTrackedIssue);

    public static async Task RaiseRowsNamedByAsync(CollectionAttemptDbContext db, DateOnly capturedOn, string articleText,
        CancellationToken cancellationToken)
    {
        if (CongressCalendar.CongressOn(capturedOn) is not { } congress) return;
        var cited = RelationshipPairRule.CitedBills(articleText);
        if (cited.IsEmpty) return;
        var numbers = cited.Select(bill => bill.Number).Distinct().ToArray();
        bool Names(int billCongress, string? billType, int? number) => cited.Any(bill =>
            RelationshipPairRule.IsSameBill(congress, bill.BillType, bill.Number, billCongress, billType, number));
        var actions = await db.Set<ActionCandidateRow>().AsNoTracking()
            .Where(candidate => candidate.Congress == congress && numbers.Contains(candidate.BillNumber))
            .Select(candidate => new { candidate.CandidateId, candidate.Congress, candidate.BillType, candidate.BillNumber }).ToArrayAsync(cancellationToken);
        var votes = await db.Set<HouseVoteCandidateRow>().AsNoTracking()
            .Where(candidate => candidate.Congress == congress && candidate.BillNumber != null && numbers.Contains(candidate.BillNumber.Value))
            .Select(candidate => new { candidate.CandidateId, candidate.Congress, candidate.BillType, candidate.BillNumber }).ToArrayAsync(cancellationToken);
        await AnalysisPriority.RaiseAsync(db, PostgresActionDraftStore.Table, "candidate_id", ActionDraftTask.Task,
            [.. actions.Where(candidate => Names(candidate.Congress, candidate.BillType, candidate.BillNumber)).Select(candidate => candidate.CandidateId)],
            ReviewPriority.Highest, cancellationToken);
        await AnalysisPriority.RaiseAsync(db, PostgresVoteDraftStore.Table, "candidate_id", VoteDraftTask.Task,
            [.. votes.Where(candidate => Names(candidate.Congress, candidate.BillType, candidate.BillNumber)).Select(candidate => candidate.CandidateId)],
            ReviewPriority.Highest, cancellationToken);
    }

    private static async Task<bool> IsNamedAsync(CollectionAttemptDbContext db, int congress, string billType, int billNumber,
        CancellationToken cancellationToken)
    {
        var mentions = await db.Database.SqlQueryRaw<MentionRow>(MentionsEndingInNumber, "% " + billNumber.ToString(CultureInfo.InvariantCulture))
            .ToArrayAsync(cancellationToken);
        return mentions.Any(mention => CongressOf(mention) is { } mentionedCongress && RelationshipPairRule.CitedBills(mention.Citation)
            .Any(bill => RelationshipPairRule.IsSameBill(mentionedCongress, bill.BillType, bill.Number, congress, billType, billNumber)));
    }

    private static int? CongressOf(MentionRow mention) => CongressCalendar.CongressOn(mention.StatementDate is { } date
        ? DateOnly.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture)
        : DateOnly.FromDateTime(new DateTime(mention.CapturedAtUtcTicks, DateTimeKind.Utc)));

    private sealed class MentionRow
    {
        public string Citation { get; set; } = "";
        public string? StatementDate { get; set; }
        public long CapturedAtUtcTicks { get; set; }
    }
}
