using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Everything that the brief page shows, read from one active release. The label is the label column of the public view.</summary>
public sealed record BriefPageData(
    int Release,
    string RecordId,
    int RevisionNumber,
    DateOnly WeekStart,
    string Headline,
    PublishedTextOrigin TextOrigin,
    string? Label,
    PublishedBriefParagraph[] Paragraphs,
    PublishedBriefCitation[] Citations)
{
    public string ShortName => PublishedBriefView.ShortName(WeekStart);
}
