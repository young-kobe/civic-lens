using CivicLens.Application.Briefs;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal static class PublishedBriefBuilder
{
    public static PublishedBrief Build(PublishableBrief brief, DateTimeOffset? firstPublishedAtUtc, PublishedCorrection[] corrections, DateTimeOffset now)
    {
        var review = brief.Review;
        var revision = review.CurrentRevision;
        var record = new PublishedBrief
        {
            RecordId = review.RecordId,
            RevisionNumber = revision.RevisionNumber,
            Headline = revision.Headline,
            WeekStart = revision.Window.WeekStart,
            CutoffReleaseNumber = revision.Window.CutoffReleaseNumber,
            Coverage = new()
            {
                Considered = revision.Coverage.Considered,
                SentToModel = revision.Coverage.SentToModel,
                Cited = revision.Coverage.Cited
            },
            Paragraphs = [.. revision.Paragraphs.Select(paragraph => new PublishedBriefParagraph
            {
                Text = paragraph.Text,
                CitationIds = [.. paragraph.CitationIds]
            })],
            Citations = [.. revision.Citations.Select(citation => new PublishedBriefCitation
            {
                Id = citation.Id,
                Kind = BriefCitationKinds.PublishedKindOf(citation.Kind),
                RecordId = citation.RecordId,
                RevisionNumber = citation.RevisionNumber,
                Headline = brief.Cited[citation.Id].Headline
            })],
            ApprovedAtUtc = PublishedRecordRules.GetApprovalTime(revision.RevisionNumber, review.Decisions),
            FirstPublishedAtUtc = firstPublishedAtUtc ?? now,
            Corrections = corrections
        };
        record.Validate();
        return record;
    }
}
