using CivicLens.Application.Briefs;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefCheckFlagRow
{
    public string RunId { get; set; } = string.Empty;
    public int FlagNumber { get; set; }
    public string RecordId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public int ParagraphIndex { get; set; }
    public BriefCheckFlagCode Code { get; set; }
    public string? CitationId { get; set; }
    public string Note { get; set; } = string.Empty;
}
