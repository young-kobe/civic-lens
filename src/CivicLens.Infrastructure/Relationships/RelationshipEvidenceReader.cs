using System.Collections.Immutable;
using CivicLens.Application.Relationships;
using CivicLens.Core.Actions;
using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Votes.Persistence;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Relationships;

internal static class RelationshipEvidenceReader
{
    private const string ActionsOfOfficials = """
        SELECT r.revision_json AS "Value" FROM action_drafts d
        JOIN action_revisions r ON r.record_id = d.record_id AND r.revision_number = d.current_revision_number
        WHERE @@scope
          AND EXISTS (SELECT 1 FROM jsonb_array_elements(r.revision_json::jsonb -> 'sponsorships') s WHERE s ->> 'personId' = ANY({0}))
        """;

    private const string StatementsOfOfficials = """
        SELECT d.candidate_id AS "Value" FROM statement_drafts d
        JOIN statement_revisions r ON r.candidate_id = d.candidate_id AND r.revision_number = d.current_revision_number
        WHERE @@scope
          AND EXISTS (SELECT 1 FROM jsonb_array_elements_text(r.revision_json::jsonb -> 'speakerOfficialIds') o WHERE o = ANY({0}))
        """;

    private const string StatementsOfOfficialsInCongress = """
        SELECT d.candidate_id AS "Value" FROM statement_drafts d
        JOIN statement_revisions r ON r.candidate_id = d.candidate_id AND r.revision_number = d.current_revision_number
        WHERE @@scope
          AND r.revision_json::jsonb ->> 'statementDate' >= {1} AND r.revision_json::jsonb ->> 'statementDate' < {2}
          AND EXISTS (SELECT 1 FROM jsonb_array_elements_text(r.revision_json::jsonb -> 'speakerOfficialIds') o WHERE o = ANY({0}))
        """;

    private const string ActionById = """
        SELECT r.revision_json AS "Value" FROM action_drafts d
        JOIN action_revisions r ON r.record_id = d.record_id AND r.revision_number = d.current_revision_number
        WHERE @@scope AND d.record_id = {0}
        """;

    private const string VotesOnBills = """
        SELECT r.revision_json AS "Value" FROM vote_drafts d
        JOIN vote_revisions r ON r.record_id = d.record_id AND r.revision_number = d.current_revision_number
        WHERE @@scope
          AND EXISTS (SELECT 1 FROM vote_candidates c
                      JOIN unnest({0}, {1}, {2}) AS b(congress, bill_type, bill_number)
                        ON c.congress = b.congress AND c.bill_type = b.bill_type AND c.bill_number = b.bill_number
                      WHERE c.source_id = d.source_id AND c.chamber = d.chamber AND c.congress = d.congress
                        AND c.session_number = d.session_number AND c.roll_call_number = d.roll_call_number)
        """;

    private const string VoteById = """
        SELECT r.revision_json AS "Value" FROM vote_drafts d
        JOIN vote_revisions r ON r.record_id = d.record_id AND r.revision_number = d.current_revision_number
        WHERE @@scope AND d.record_id = {0}
        """;

    public static async Task<ImmutableArray<ApprovedStatement>> ReadStatementsAsync(CollectionAttemptDbContext db, string[]? candidateIds,
        RelationshipRevisionScope scope, CancellationToken cancellationToken)
    {
        var rows = await (from draft in db.Set<StatementDraftRow>().AsNoTracking()
                          where (scope == RelationshipRevisionScope.Latest || draft.ReviewStatus == ReviewStatus.Approved) && (candidateIds == null || candidateIds.Contains(draft.CandidateId))
                          join revision in db.Set<StatementRevisionRow>().AsNoTracking()
                              on new { draft.CandidateId, Number = draft.CurrentRevisionNumber } equals new { revision.CandidateId, Number = revision.RevisionNumber }
                          join candidate in db.Set<StatementCandidateRow>().AsNoTracking() on draft.CandidateId equals candidate.CandidateId
                          join extraction in db.Set<DocumentExtractionRow>().AsNoTracking() on candidate.ExtractionId equals extraction.ExtractionId
                          select new { revision.RevisionJson, extraction.Text }).ToArrayAsync(cancellationToken);
        return [.. rows.Select(row => new ApprovedStatement(
            ReviewJson.Deserialize<StatementDraftRevision>(row.RevisionJson, "statement revision"), row.Text))];
    }

    public static async Task<ImmutableArray<ApprovedStatement>> ReadStatementsOfOfficialsAsync(CollectionAttemptDbContext db,
        string[] officialIds, RelationshipRevisionScope scope, CancellationToken cancellationToken) =>
        await ReadStatementsAsync(db, await db.Database.SqlQueryRaw<string>(Scoped(StatementsOfOfficials, scope), (object)officialIds)
            .ToArrayAsync(cancellationToken), scope, cancellationToken);

    public static async Task<ImmutableArray<ApprovedStatement>> ReadDatedStatementsOfOfficialsAsync(CollectionAttemptDbContext db,
        string[] officialIds, int congress, RelationshipRevisionScope scope, CancellationToken cancellationToken)
    {
        var from = CongressCalendar.StartOf(congress).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var until = CongressCalendar.StartOf(congress + 1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        return await ReadStatementsAsync(db, await db.Database.SqlQueryRaw<string>(Scoped(StatementsOfOfficialsInCongress, scope), officialIds,
            from, until).ToArrayAsync(cancellationToken), scope, cancellationToken);
    }

    public static Task<bool> IsApprovedAsync(CollectionAttemptDbContext db, RelationshipRecordKind kind, string recordId,
        CancellationToken cancellationToken) => kind switch
        {
            RelationshipRecordKind.Statement => db.Set<StatementDraftRow>().AsNoTracking()
                .AnyAsync(draft => draft.CandidateId == recordId && draft.ReviewStatus == ReviewStatus.Approved, cancellationToken),
            RelationshipRecordKind.Action => db.Set<ActionDraftRow>().AsNoTracking()
                .AnyAsync(draft => draft.RecordId == recordId && draft.ReviewStatus == ReviewStatus.Approved, cancellationToken),
            RelationshipRecordKind.Vote => db.Set<VoteDraftRow>().AsNoTracking()
                .AnyAsync(draft => draft.RecordId == recordId && draft.ReviewStatus == ReviewStatus.Approved, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Relationship record kind has no draft table.")
        };

    public static async Task<ImmutableArray<ActionDraftRevision>> ReadActionsSponsoredByAsync(CollectionAttemptDbContext db,
        string[] officialIds, RelationshipRevisionScope scope, CancellationToken cancellationToken) =>
        Parse<ActionDraftRevision>(await db.Database.SqlQueryRaw<string>(Scoped(ActionsOfOfficials, scope), (object)officialIds)
            .ToArrayAsync(cancellationToken), "action revision");

    public static async Task<ActionDraftRevision?> ReadActionAsync(CollectionAttemptDbContext db, string recordId,
        RelationshipRevisionScope scope, CancellationToken cancellationToken) =>
        Parse<ActionDraftRevision>(await db.Database.SqlQueryRaw<string>(Scoped(ActionById, scope), recordId).ToArrayAsync(cancellationToken),
            "action revision").SingleOrDefault();

    public static async Task<ImmutableArray<VoteDraftRevision>> ReadVotesOnAsync(CollectionAttemptDbContext db,
        ImmutableArray<(int Congress, string BillType, int Number)> bills, RelationshipRevisionScope scope, CancellationToken cancellationToken) =>
        bills.IsEmpty ? []
            : Parse<VoteDraftRevision>(await db.Database.SqlQueryRaw<string>(Scoped(VotesOnBills, scope), bills.Select(bill => bill.Congress).ToArray(),
                bills.Select(bill => bill.BillType).ToArray(), bills.Select(bill => bill.Number).ToArray()).ToArrayAsync(cancellationToken),
                "vote revision");

    public static async Task<VoteDraftRevision?> ReadVoteAsync(CollectionAttemptDbContext db, string recordId,
        RelationshipRevisionScope scope, CancellationToken cancellationToken) =>
        Parse<VoteDraftRevision>(await db.Database.SqlQueryRaw<string>(Scoped(VoteById, scope), recordId).ToArrayAsync(cancellationToken),
            "vote revision").SingleOrDefault();

    private static string Scoped(string sql, RelationshipRevisionScope scope) =>
        sql.Replace("@@scope", scope == RelationshipRevisionScope.Approved ? "d.review_status = 'Approved'" : "TRUE", StringComparison.Ordinal);

    private static ImmutableArray<T> Parse<T>(string[] json, string kind) => [.. json.Select(item => ReviewJson.Deserialize<T>(item, kind))];
}
