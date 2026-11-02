using CivicLens.Application.Briefs;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefCheckFlagRow
{
    public string RunId { get; set; } = string.Empty;
    public int FlagNumber { get; set; }
    public int ParagraphIndex { get; set; }
    public BriefCheckFlagCode Code { get; set; }
    public string? CitationId { get; set; }
    public string Message { get; set; } = string.Empty;
}
