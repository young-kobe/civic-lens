using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Relationships;

public sealed class GetRelationshipDraftStatus(
    GetAnalysisStatus<RelationshipDraftRun, RelationshipDraftView, RelationshipDraftListItem> queue, IRelationshipDraftStore store)
{
    public async Task<RelationshipDraftStatusReport> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return new(await queue.ExecuteAsync(actor, cancellationToken), await store.CountStatementsSkippedWithoutDateAsync(cancellationToken));
    }
}
