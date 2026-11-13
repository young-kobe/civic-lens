using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Everything that the brief page shows, read from one active release. The label is the label column of the public view. The coverage and the two times are the columns of the same view. <c>Cited</c> counts the listed citations.</summary>
public sealed record BriefPageData(
    int Release,
    string RecordId,
    int RevisionNumber,
    DateOnly WeekStart,
    string Headline,
    PublishedTextOrigin TextOrigin,
    string? Label,
    PublishedBriefParagraph[] Paragraphs,
    PublishedBriefCitation[] Citations,
    int CutoffReleaseNumber,
    PublishedBriefCoverage Coverage,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? FirstPublishedAtUtc)
{
    /// <summary>The correction history of the shown revision in the active release, newest first. The correction banner shows the newest.</summary>
    public PublishedCorrection[] Corrections { get; init; } = [];

    public string ShortName => PublishedBriefView.ShortName(WeekStart);
}
