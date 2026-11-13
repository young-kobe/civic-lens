using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Everything that the executive action page shows, read from one active release. The Federal Register document gives every field except the official ID of the President.</summary>
public sealed record ExecutiveActionPageData(
    int Release,
    string RecordId,
    int RevisionNumber,
    string Headline,
    PublishedExecutiveActionKind Kind,
    string DocumentNumber,
    DateOnly SignedOn,
    DateOnly PublishedOn,
    string FederalRegisterCitation,
    string DocumentUrl,
    string OfficialId,
    string PresidentName,
    ExecutiveActionNoticeData[] Notices,
    ExecutiveActionTargetData[] Targets,
    ExecutiveActionSummaryData? Summary = null)
{
    /// <summary>The correction history of the shown revision in the active release, newest first. The correction banner shows the newest.</summary>
    public PublishedCorrection[] Corrections { get; init; } = [];

    /// <summary>The cited facts of the revision in the order of <see cref="PublishedExecutiveAction.Facts"/>. Each cites one of <see cref="Sources"/>.</summary>
    public PublishedExecutiveActionFact[] Facts { get; init; } = [];

    /// <summary>The saved Federal Register records that the facts cite, in the order of their first citation. The body text is not one of them.</summary>
    public PublishedDocumentVersion[] Sources { get; init; } = [];
}
