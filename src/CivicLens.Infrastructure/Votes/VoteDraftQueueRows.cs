using CivicLens.Application.Analysis;
using CivicLens.Application.Votes;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Votes;
using CivicLens.Infrastructure.Votes.Persistence;

namespace CivicLens.Infrastructure.Votes;

internal static class VoteDraftQueueRows
{
    public static void Add(CollectionAttemptDbContext db, string candidateId, long nowTicks, int priority) => db.Add(new VoteAnalysisRow
    {
        CandidateId = candidateId,
        Task = VoteDraftTask.Task,
        Status = AnalysisState.Pending,
        CreatedAtUtcTicks = nowTicks,
        Priority = priority
    });

    public static IQueryable<VoteCandidateRow> WaitingForBill(CollectionAttemptDbContext db) =>
        db.Set<VoteCandidateRow>().Where(candidate => candidate.BillType != null &&
            !db.Set<VoteAnalysisRow>().Any(entry => entry.CandidateId == candidate.CandidateId));
}
