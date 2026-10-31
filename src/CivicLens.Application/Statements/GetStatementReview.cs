using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Statements;

public sealed class GetStatementReview(IStatementReviewStore store, StatementSelectionResolver selections)
{
    public async Task<StatementReviewDetail?> ExecuteAsync(ReviewActor actor, string candidateId, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        var read = await store.GetAsync(ReviewValidation.ValidateCandidateId(candidateId), cancellationToken);
        if (read is null) return null;
        var resolved = selections.Resolve(read.ConfigurationRevision, read.SourceId, read.FirstCapturedOn);
        var approval = read.Review.CurrentRevision is { } revision
            ? ApprovalCheck.Of(() => resolved.Allowed.RequireApprovable(revision, read.Extraction)) : null;
        return new(read.Review, read.SourceId, read.Url, read.Extraction, read.AnalysisRunId, resolved.Allowed, resolved.Officials, approval);
    }
}
