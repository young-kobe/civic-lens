using CivicLens.Publication.Contracts;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseOfficialRow
{
    public int ReleaseNumber { get; set; }
    public string OfficialId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? WikidataId { get; set; }
    public string? Website { get; set; }
}

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

internal sealed class ReleaseIssueRow
{
    public int ReleaseNumber { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Definition { get; set; }
}

internal abstract class ReleaseIssueTermRow
{
    public int ReleaseNumber { get; set; }
    public string IssueId { get; set; } = string.Empty;
    public short Position { get; set; }
    public string Text { get; set; } = string.Empty;
}

internal sealed class ReleaseIssueIncludeRow : ReleaseIssueTermRow;

internal sealed class ReleaseIssueExcludeRow : ReleaseIssueTermRow;

internal sealed class ReleasePolicyAreaRow
{
    public int ReleaseNumber { get; set; }
    public string PolicyAreaId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
