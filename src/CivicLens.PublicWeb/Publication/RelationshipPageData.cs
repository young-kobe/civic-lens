using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Everything that the relationship page shows, read from one active release. The reason, the quote, and the fact come from the exact published
/// relationship revision. The quote is the exact text of the statement source, and the fact is the exact fact of the cited target revision.
/// The label is the label column of the public view. The fact text origin is null when the fact has no label. A time that the view does not hold is null, and the page shows no row for it.</summary>
public sealed record RelationshipPageData(
    int Release,
    string RecordId,
    int RevisionNumber,
    string Headline,
    PublishedRelationshipType Type,
    DateOnly? StatementDate,
    string Reason,
    PublishedTextOrigin TextOrigin,
    string? Label,
    RelationshipEndData Statement,
    RelationshipEndData Target,
    string Quote,
    string Fact,
    string? FactLabel,
    PublishedTextOrigin? FactTextOrigin,
    RecordOfficialData[] Speakers,
    RecordIssueData[] Issues,
    DateTimeOffset? ApprovedAtUtc,
    DateTimeOffset? FirstPublishedAtUtc)
{
    /// <summary>The correction history of the shown revision in the active release, newest first. The correction banner shows the newest.</summary>
    public PublishedCorrection[] Corrections { get; init; } = [];

    /// <summary>The speaker names for the trail. Null when the release names no speaker, so the trail ends with the headline.</summary>
    public string? ShortName => Speakers.Length == 0 ? null : string.Join(", ", Speakers.Select(speaker => speaker.Name));
}
