using CivicLens.Application.Actions;
using CivicLens.Application.Analysis;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Collection;

namespace CivicLens.Infrastructure.Actions;

internal static class ActionDraftQueueRows
{
    public static void Add(CollectionAttemptDbContext db, string candidateId, long nowTicks, int priority) => db.Add(new ActionAnalysisRow
    {
        CandidateId = candidateId,
        Task = ActionDraftTask.Task,
        Status = AnalysisState.Pending,
        CreatedAtUtcTicks = nowTicks,
        Priority = priority
    });
}
