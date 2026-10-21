using CivicLens.Application.Actions;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedActionBuilder(PublishedRecordRules rules)
{
    public PublishedAction Build(PublishableAction action, PublishedRecordEntry? previous, PublishedCorrection[] corrections, DateTimeOffset now)
    {
        var (review, extractions) = action;
        var revision = review.CurrentRevision;
        PublishedCitation Cite(SourceSpan span) => PublishedRecordRules.Cite(span, extractions[span.ExtractionId]);

        var record = new PublishedAction
        {
            RecordId = review.RecordId,
            RevisionNumber = revision.RevisionNumber,
            Headline = revision.Headline,
            Summary = revision.Summary,
            TextOrigin = PublishedRecordRules.TextOriginOf(revision.TextOrigin),
            Congress = revision.Congress,
            BillType = revision.BillType,
            BillNumber = revision.BillNumber,
            BillTitle = revision.BillTitle,
            PolicyArea = revision.PolicyArea,
            PolicyAreaId = revision.PolicyAreaId,
            PolicyAreaCitation = revision.PolicyAreaCitation is { } area ? Cite(area) : null,
            TrackedIssueIds = [.. revision.TrackedIssueIds],
            SummaryCitation = revision.SummaryCitation is { } summary ? Cite(summary) : null,
            Sponsorships = [.. revision.Sponsorships.Select(sponsorship => new PublishedSponsorship
            {
                Official = rules.MapOfficial(review.RecordId, sponsorship.PersonId),
                Role = RoleOf(sponsorship.Role),
                IsOriginal = sponsorship.IsOriginal,
                SponsoredOn = sponsorship.SponsoredOn,
                WithdrawnOn = sponsorship.WithdrawnOn,
                Citations = [.. sponsorship.Citations.Select(Cite)]
            })],
            Sources = PublishedRecordRules.Sources(revision.Citations, extractions),
            ApprovedAtUtc = PublishedRecordRules.GetApprovalTime(revision.RevisionNumber, review.Decisions),
            FirstPublishedAtUtc = previous?.FirstPublishedAtUtc ?? now,
            Corrections = corrections
        };
        record.Validate();
        return record;
    }

    private static PublishedSponsorshipRole RoleOf(ActionSponsorshipRole role) => role switch
    {
        ActionSponsorshipRole.Sponsor => PublishedSponsorshipRole.Sponsor,
        ActionSponsorshipRole.Cosponsor => PublishedSponsorshipRole.Cosponsor,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Sponsorship role has no published form.")
    };
}
