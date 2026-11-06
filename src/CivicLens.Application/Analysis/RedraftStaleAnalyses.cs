using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public sealed class RedraftStaleAnalyses(IReadOnlyList<StaleAnalysisStage> stages)
{
    public async Task<IReadOnlyDictionary<string, int>> ExecuteAsync(ReviewActor actor, string? stageName,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var selected = stageName is null ? stages : [.. stages.Where(stage => stage.Name == stageName)];
        if (selected.Count == 0)
            throw new ArgumentException($"Unknown stage. Use one of: {string.Join(", ", stages.Select(stage => stage.Name))}.", nameof(stageName));
        var requeued = new Dictionary<string, int>();
        foreach (var stage in selected) requeued[stage.Name] = await stage.Store.RequeueStaleAsync(cancellationToken);
        return requeued;
    }
}
