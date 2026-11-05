using CivicLens.Publication.Contracts;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseOfficialTermRow
{
    public int ReleaseNumber { get; set; }
    public string OfficialId { get; set; } = string.Empty;
    public short Position { get; set; }
    public string Office { get; set; } = string.Empty;
    public PublishedOfficeKind Kind { get; set; }
    public string? State { get; set; }
    public int? District { get; set; }
    public string? Party { get; set; }
    public string? Caucus { get; set; }
    public DateOnly StartOn { get; set; }
    public DateOnly? EndOn { get; set; }
}
