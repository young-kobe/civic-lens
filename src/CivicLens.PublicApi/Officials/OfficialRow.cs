using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Officials;

/// <summary>
/// One row of the view <c>public_api.officials</c>. The term fields come from the latest term that started on or before the day of the release.
/// <c>RecordCount</c> counts the records of the active release that name the official. A vote record names no official, so no vote counts.
/// </summary>
public sealed record OfficialRow
{
    public required string OfficialId { get; init; }
    public required string Name { get; init; }
    public PublishedOfficeKind? Kind { get; init; }
    public string? Office { get; init; }
    public string? State { get; init; }
    public int? District { get; init; }
    public string? Party { get; init; }
    public string? Caucus { get; init; }
    public DateOnly? TermEndsOn { get; init; }
    public required int RecordCount { get; init; }
    public string? PhotoPath { get; init; }
}
