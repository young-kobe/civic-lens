using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Relationships;
using CivicLens.Application.Votes;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Votes;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Relationships;

// The exact tier of a saved draft. It reads the latest revisions with no approval filter, so a pair counts before a person decides.
internal static class PairPriority
{
    private const string ActionCandidatesOfRecords = """
        SELECT c.candidate_id AS "Value" FROM action_candidates c
        JOIN action_drafts d ON d.source_id = c.source_id AND d.congress = c.congress AND d.bill_type = c.bill_type AND d.bill_number = c.bill_number
        WHERE d.record_id = ANY({0})
        """;

    private const string VoteCandidatesOfRecords = """
        SELECT c.candidate_id AS "Value" FROM house_vote_candidates c
        JOIN vote_drafts d ON d.source_id = c.source_id AND d.congress = c.congress AND d.session_number = c.session_number
                          AND d.roll_call_number = c.roll_call_number
        WHERE d.record_id = ANY({0})
        """;

    public static async Task<int> OfTargetAsync(CollectionAttemptDbContext db, RelationshipRecordKind kind, string recordId, bool hasTrackedIssue,
        CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        var pairs = await RelationshipPairSource.FindAsync(db, kind, recordId, RelationshipRevisionScope.Latest, cancellationToken);
        return ReviewPriority.Of(!pairs.IsEmpty, hasTrackedIssue);
    }

    public static async Task RaiseTargetsOfStatementAsync(CollectionAttemptDbContext db, string statementId, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        var pairs = await RelationshipPairSource.FindAsync(db, RelationshipRecordKind.Statement, statementId, RelationshipRevisionScope.Latest,
            cancellationToken);
        await RaiseAsync(db, pairs, RelationshipRecordKind.Action, ActionCandidatesOfRecords, PostgresActionDraftStore.Table,
            ActionDraftTask.Task, cancellationToken);
        await RaiseAsync(db, pairs, RelationshipRecordKind.Vote, VoteCandidatesOfRecords, PostgresVoteDraftStore.Table, VoteDraftTask.Task,
            cancellationToken);
    }

    private static async Task RaiseAsync(CollectionAttemptDbContext db, ImmutableArray<RelationshipPair> pairs, RelationshipRecordKind kind,
        string candidatesSql, string table, string task, CancellationToken cancellationToken)
    {
        var recordIds = pairs.Where(pair => pair.Target.Kind == kind).Select(pair => pair.Target.RecordId).Distinct().ToArray();
        if (recordIds.Length == 0) return;
        var candidateIds = await db.Database.SqlQueryRaw<string>(candidatesSql, (object)recordIds).ToArrayAsync(cancellationToken);
        await AnalysisPriority.RaiseAsync(db, table, "candidate_id", task, candidateIds, ReviewPriority.Highest, cancellationToken);
    }
}
