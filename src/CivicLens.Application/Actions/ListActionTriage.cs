using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class ListActionTriage(IActionReviewStore store)
{
    public async Task<ReviewTriagePage<ActionReview>> ExecuteAsync(ReviewActor actor, string? cursor, DraftListFilter filter = default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ReviewValidation.ValidateListFilter(filter);
        var read = await store.ListTriageAsync(ReviewValidation.ValidatePage(cursor, ReviewTriage.PageSize), filter, cancellationToken);
        return new([.. read.Items.Select(review => RowOf(review, read))], read.NewerCursor, read.OlderCursor);
    }

    private static ReviewTriageRow<ActionReview> RowOf(ActionReview review, ReviewTriageRead<ActionReview> read)
    {
        var revision = review.CurrentRevision;
        var check = ApprovalCheck.Of(() => revision.ValidateForApproval(read.Extractions));
        return new(review, check, check.Passed ? QuotesOf(revision, read) : [], read.Priorities[review.RecordId]);
    }

    private static ImmutableArray<ReviewTriageQuote> QuotesOf(ActionDraftRevision revision, ReviewTriageRead<ActionReview> read)
    {
        var quotes = ImmutableArray.CreateBuilder<ReviewTriageQuote>();
        foreach (var sponsorship in revision.Sponsorships)
            quotes.Add(ReviewTriageQuote.Of(RoleLabel(sponsorship), sponsorship.Citations[0], read.Extractions));
        if (revision.PolicyAreaCitation is { } area) quotes.Add(ReviewTriageQuote.Of("Policy area", area, read.Extractions));
        if (revision.SummaryCitation is { } summary) quotes.Add(ReviewTriageQuote.Of("CRS summary", summary, read.Extractions));
        return quotes.ToImmutable();
    }

    private static string RoleLabel(ActionSponsorship sponsorship) => sponsorship.Role == ActionSponsorshipRole.Sponsor ? "Sponsor"
        : sponsorship.IsOriginal ? "Original cosponsor" : "Cosponsor";
}
