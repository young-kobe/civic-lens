using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed class VerifyRevisions(IRevisionBackfillStore store)
{
    private const int ReportedMismatches = 20;

    public Task<RevisionVerification> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        return store.VerifyAsync(ReportedMismatches, cancellationToken);
    }
}
