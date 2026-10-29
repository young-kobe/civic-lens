using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using CivicLens.Core.Relationships;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Votes;
using CivicLens.Infrastructure.Review;
using CivicLens.Infrastructure.Votes.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using VoteQueue = CivicLens.Infrastructure.Analysis.PostgresAnalysisQueue<
    CivicLens.Infrastructure.Votes.Persistence.VoteAnalysisRow,
    CivicLens.Infrastructure.Votes.Persistence.VoteAnalysisBudgetRow,
    CivicLens.Application.Votes.VoteDraftRun>;

namespace CivicLens.Infrastructure.Votes;

internal static class VoteDraftStaleness
{
    private static readonly AnalysisState[] RequeuedStates = [AnalysisState.Blocked];

    public static VoteQueue NewQueue(IDbContextFactory<CollectionAttemptDbContext> contextFactory) =>
        new(contextFactory, new VoteDraftQueueMapping(AfterDecisionHooks.ForRecordKind(contextFactory, RelationshipRecordKind.Vote, PublishedRecordKind.Vote)));

    /// <summary>A new roster snapshot can name a senator that an earlier Senate draft could not match. A candidate of a disabled source stays blocked.</summary>
    public static async Task<int> RequeueSenateUnmatchedAsync(VoteQueue queue, CollectionAttemptDbContext db, CancellationToken cancellationToken)
    {
        var candidateIds = await (from entry in db.Set<VoteAnalysisRow>()
                                  join candidate in db.Set<VoteCandidateRow>().OfEnabledSources(db) on entry.CandidateId equals candidate.CandidateId
                                  where entry.Task == VoteDraftTask.Task && entry.Status == AnalysisState.Blocked &&
                                      entry.ErrorCode == VoteDraftErrorCodes.EvidenceUnavailable && candidate.Chamber == Chamber.Senate
                                  select entry.CandidateId).ToArrayAsync(cancellationToken);
        return candidateIds.Length == 0 ? 0 : await queue.RequeueInAsync(db, RequeuedStates, candidateIds, cancellationToken);
    }
}
