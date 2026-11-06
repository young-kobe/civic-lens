using CivicLens.Application.Actions;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PublishedActionBuilder(PublishedRecordRules rules)
{
    public PublishedAction Build(PublishableAction action, DateTimeOffset? firstPublishedAtUtc, PublishedCorrection[] corrections, DateTimeOffset now)
    {
        var (review, extractions, textVersion) = action;
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
            BillCitation = revision.BillCitation is { } bill ? Cite(bill) : null,
            BillText = BillTextOf(revision, textVersion, Cite),
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
            FirstPublishedAtUtc = firstPublishedAtUtc ?? now,
            Corrections = corrections
        };
        record.Validate();
        return record;
    }

    private static PublishedBillText? BillTextOf(ActionDraftRevision revision, ActionTextVersion? version, Func<SourceSpan, PublishedCitation> cite)
    {
        if (revision.TextPackageId is null) return null;
        if (version?.PackageId != revision.TextPackageId)
            throw new InvalidOperationException("A text draft is published with the stored bill text version that it cites.");
        return new PublishedBillText
        {
            VersionName = version.VersionType,
            VersionDate = version.VersionDate,
            CharsRead = version.CharsRead,
            CharsTotal = version.CharsTotal,
            Sentences = [.. revision.TextCitations.Select(item => new PublishedTextSentence
            {
                Part = item.Part == ActionTextPart.Summary ? PublishedTextPart.Summary : PublishedTextPart.WhyItMatters,
                Sentence = item.Sentence,
                Citation = cite(item.Span)
            })]
        };
    }

    private static PublishedSponsorshipRole RoleOf(ActionSponsorshipRole role) => role switch
    {
        ActionSponsorshipRole.Sponsor => PublishedSponsorshipRole.Sponsor,
        ActionSponsorshipRole.Cosponsor => PublishedSponsorshipRole.Cosponsor,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Sponsorship role has no published form.")
    };
}
