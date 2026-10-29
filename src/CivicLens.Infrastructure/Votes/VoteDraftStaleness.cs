using CivicLens.Core.Relationships;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Review;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using VoteQueue = CivicLens.Infrastructure.Analysis.PostgresAnalysisQueue<
    CivicLens.Infrastructure.Votes.Persistence.VoteAnalysisRow,
    CivicLens.Infrastructure.Votes.Persistence.VoteAnalysisBudgetRow,
    CivicLens.Application.Votes.VoteDraftRun>;

namespace CivicLens.Infrastructure.Votes;

internal static class VoteDraftStaleness
{
    public static VoteQueue NewQueue(IDbContextFactory<CollectionAttemptDbContext> contextFactory) =>
        new(contextFactory, new VoteDraftQueueMapping(AfterDecisionHooks.ForRecordKind(contextFactory, RelationshipRecordKind.Vote, PublishedRecordKind.Vote)));
}
