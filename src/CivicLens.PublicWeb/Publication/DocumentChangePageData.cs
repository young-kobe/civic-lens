using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Everything that the document change page shows, read from one active release. The label is the label column of the public view. The two versions hold the saved texts that the changes and citations point into.</summary>
public sealed record DocumentChangePageData(
    int Release,
    string RecordId,
    int RevisionNumber,
    string Headline,
    string Summary,
    PublishedTextOrigin TextOrigin,
    string? Label,
    string? Significance,
    string? Limits,
    string Institution,
    DateOnly? ChangeDate,
    RecordOfficialData[] Officials,
    RecordIssueData[] Issues,
    PublishedDocumentVersion Before,
    PublishedDocumentVersion After,
    PublishedChange[] Changes,
    PublishedCitation[] Citations,
    PublishedCitation? ChangeDateEvidence)
{
    /// <summary>The correction history of the shown revision in the active release, newest first. The correction banner shows the newest.</summary>
    public PublishedCorrection[] Corrections { get; init; } = [];
}
