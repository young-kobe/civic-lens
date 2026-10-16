using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class SaveActionDraft(IActionReviewStore store)
{
    public async Task<ActionDraftRevision> ExecuteAsync(ReviewActor actor, SaveActionDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var recordId = ReviewValidation.ValidateHash(request.RecordId, nameof(request.RecordId));
        if (request.ExpectedRevisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(request.ExpectedRevisionNumber));
        // The edit copies the expected revision. The store compares the edit with the current revision under the review lock.
        var expected = await store.GetRevisionAsync(recordId, request.ExpectedRevisionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Action draft revision was not found.");
        var revision = expected with
        {
            RevisionNumber = expected.RevisionNumber + 1,
            AuthorSubject = actor.Subject,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Headline = request.Headline,
            Summary = request.Summary,
            TextOrigin = ReviewTextOrigin.Person,
            Suggestion = request.KeepSuggestion ? expected.Suggestion : null
        };
        revision.ValidatePersonEditOf(expected);
        return await store.SaveRevisionAsync(actor.Subject, revision, request.ExpectedRevisionNumber, request.IdempotencyKey,
            ReviewValidation.HashPayload(request), cancellationToken);
    }
}
