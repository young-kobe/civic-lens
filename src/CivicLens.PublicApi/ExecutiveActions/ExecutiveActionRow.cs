using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>One row of the view <c>public_api.executive_actions</c>. The official ID is the configured ID of the President. The Federal Register wrote every field.</summary>
public sealed record ExecutiveActionRow
{
    public required string RecordId { get; init; }
    public required int RevisionNumber { get; init; }
    public required string Headline { get; init; }
    public required PublishedExecutiveActionKind Kind { get; init; }
    public required string DocumentNumber { get; init; }
    public required DateOnly SignedOn { get; init; }
    public required DateOnly PublishedOn { get; init; }
    public required string FederalRegisterCitation { get; init; }
    public required string DocumentUrl { get; init; }
    public required string OfficialId { get; init; }
    public required string PresidentName { get; init; }
}
