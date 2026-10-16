using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;

namespace CivicLens.Application.Briefs;

public sealed class SaveBriefDraft(IBriefReviewStore store)
{
    public async Task<BriefRevision> ExecuteAsync(ReviewActor actor, SaveBriefDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ArgumentNullException.ThrowIfNull(request);
        ReviewValidation.ValidateIdempotencyKey(request.IdempotencyKey);
        var recordId = ReviewValidation.ValidateHash(request.RecordId, nameof(request.RecordId));
        if (request.ExpectedRevisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(request.ExpectedRevisionNumber));
        if (request.Paragraphs.IsDefault) throw new ArgumentException("A brief needs paragraphs.", nameof(request));
        var expected = await store.GetRevisionAsync(recordId, request.ExpectedRevisionNumber, cancellationToken)
            ?? throw new KeyNotFoundException("Brief revision was not found.");
        var used = request.Suggestion == SuggestionChoice.Use ? ReviewValidation.RequireSuggestion(expected.Suggestion) : null;
        var paragraphs = used?.Paragraphs ?? request.Paragraphs;
        var citations = used?.Citations ?? CitationsUsedBy(paragraphs, expected);
        var revision = expected with
        {
            RevisionNumber = expected.RevisionNumber + 1,
            AuthorSubject = actor.Subject,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Headline = used?.Headline ?? request.Headline,
            TextOrigin = ReviewTextOrigin.Person,
            Suggestion = request.Suggestion == SuggestionChoice.Keep ? expected.Suggestion : null,
            Paragraphs = paragraphs,
            Citations = citations,
            Coverage = expected.Coverage with { Cited = citations.Length }
        };
        revision.ValidatePersonEditOf(expected);
        return await store.SaveRevisionAsync(actor.Subject, revision, request.ExpectedRevisionNumber, request.IdempotencyKey,
            ReviewValidation.HashPayload(request), cancellationToken);
    }

    private static ImmutableArray<BriefCitation> CitationsUsedBy(
        ImmutableArray<BriefParagraph> paragraphs, BriefRevision expected)
    {
        var used = paragraphs.SelectMany(paragraph => paragraph.CitationIds.IsDefault ? [] : paragraph.CitationIds)
            .ToHashSet(StringComparer.Ordinal);
        return [.. expected.Citations.Where(citation => used.Contains(citation.Id))];
    }
}
