using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed class ListVoteTriage(IVoteReviewStore store)
{
    public async Task<ReviewTriagePage<VoteReview>> ExecuteAsync(ReviewActor actor, string? cursor, DraftListFilter filter = default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ReviewValidation.ValidateListFilter(filter);
        var read = await store.ListTriageAsync(ReviewValidation.ValidatePage(cursor, ReviewTriage.PageSize), filter, cancellationToken);
        return new([.. read.Items.Select(review => RowOf(review, read))], read.NewerCursor, read.OlderCursor);
    }

    private static ReviewTriageRow<VoteReview> RowOf(VoteReview review, ReviewTriageRead<VoteReview> read)
    {
        var revision = review.CurrentRevision;
        var check = ApprovalCheck.Of(() => revision.ValidateForApproval(read.Extractions));
        return new(review, check, check.Passed ? QuotesOf(revision, read) : [], read.Priorities[review.RecordId]);
    }

    private static ImmutableArray<ReviewTriageQuote> QuotesOf(VoteDraftRevision revision, ReviewTriageRead<VoteReview> read)
    {
        var quotes = ImmutableArray.CreateBuilder<ReviewTriageQuote>();
        quotes.Add(ReviewTriageQuote.Of("Vote day", revision.VotedOnCitation, read.Extractions));
        if (revision.KindCitation is { } kind) quotes.Add(ReviewTriageQuote.Of("Vote kind", kind, read.Extractions));
        foreach (var fact in revision.Facts) quotes.Add(ReviewTriageQuote.Of("Fact", fact.Citation, read.Extractions));
        return quotes.ToImmutable();
    }
}
