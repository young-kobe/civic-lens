using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed class SaveVoteDraft(IVoteReviewStore store)
{
    public async Task<VoteDraftRevision> ExecuteAsync(ReviewActor actor, SaveVoteDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var recordId = ReviewValidation.ValidateHash(request.RecordId, nameof(request.RecordId));
        if (request.ExpectedRevisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(request.ExpectedRevisionNumber));
        var expected = await store.GetRevisionAsync(recordId, request.ExpectedRevisionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Vote draft revision was not found.");
        var used = request.Suggestion == SuggestionChoice.Use ? ReviewValidation.RequireSuggestion(expected.Suggestion) : null;
        var revision = expected with
        {
            RevisionNumber = expected.RevisionNumber + 1,
            AuthorSubject = actor.Subject,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Headline = used?.Headline ?? request.Headline,
            Summary = used?.Summary ?? request.Summary,
            TextOrigin = ReviewTextOrigin.Person,
            Suggestion = request.Suggestion == SuggestionChoice.Keep ? expected.Suggestion : null
        };
        revision.ValidatePersonEditOf(expected);
        return await store.SaveRevisionAsync(actor.Subject, revision, request.ExpectedRevisionNumber, request.IdempotencyKey,
            ReviewValidation.HashPayload(request), cancellationToken);
    }
}
