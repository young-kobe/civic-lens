using CivicLens.Application.Analysis;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Votes;

public sealed class GetVoteDraftStatus(GetAnalysisStatus<VoteDraftRun, VoteDraftView, VoteDraftListItem> queue, IVoteDraftStore store)
{
    public async Task<VoteDraftStatusReport> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return new(await queue.ExecuteAsync(actor, cancellationToken), await store.CountWaitingForBillAsync(cancellationToken));
    }
}
