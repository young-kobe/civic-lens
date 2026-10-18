using CivicLens.Application.Review;
using CivicLens.Core.Relationships;
using CivicLens.Core.Review;

namespace CivicLens.Application.Relationships;

public sealed class SaveRelationshipDraft(IRelationshipReviewStore store)
{
    public async Task<RelationshipRevision> ExecuteAsync(ReviewActor actor, SaveRelationshipDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var recordId = ReviewValidation.ValidateHash(request.RecordId, nameof(request.RecordId));
        if (request.ExpectedRevisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(request.ExpectedRevisionNumber));
        ArgumentNullException.ThrowIfNull(request.StatementQuote);
        var expected = await store.GetRevisionAsync(recordId, request.ExpectedRevisionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Relationship revision was not found.");
        var revision = expected with
        {
            RevisionNumber = expected.RevisionNumber + 1,
            AuthorSubject = actor.Subject,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Type = request.Type,
            Reason = request.Reason,
            StatementQuote = request.StatementQuote,
            TargetFactId = request.TargetFactId
        };
        revision.ValidatePersonEditOf(expected);
        return await store.SaveRevisionAsync(actor.Subject, revision, request.ExpectedRevisionNumber, request.IdempotencyKey,
            ReviewValidation.HashPayload(request), cancellationToken);
    }
}
