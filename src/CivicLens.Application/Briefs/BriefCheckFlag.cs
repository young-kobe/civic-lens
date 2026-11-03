namespace CivicLens.Application.Briefs;

public sealed record BriefCheckFlag(int ParagraphIndex, BriefCheckFlagCode Code, string? CitationId, string Note)
{
    public const int MaximumNoteLength = 300;
}

public enum BriefCheckFlagCode
{
    UnsupportedByCitation,
    Overstated,
    StatesBillStatus,
    LoadedWording,
    OmitsNotableRecord,
    Other
}
