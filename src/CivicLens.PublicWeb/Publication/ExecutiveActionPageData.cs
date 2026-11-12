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
    ExecutiveActionNoticeData[] Notices);
