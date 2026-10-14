namespace CivicLens.Application.Actions;

public static class ActionDraftErrorCodes
{
    public const string EvidenceUnavailable = "evidenceUnavailable";
    public const string RenderedTextMismatch = "renderedTextMismatch";
    public const string ExtractionVersionChanged = "extractionVersionChanged";
    public const string TooManySummaryPages = "tooManySummaryPages";
    public const string OverByteCap = "overByteCap";
    public const string TooManySponsorships = "tooManySponsorships";
    public const string TitleTooLong = "titleTooLong";
    public const string NoTrackedOfficial = "noTrackedOfficial";
    public const string SummaryOmitted = "summaryOmitted";
    public const string Unchanged = "unchanged";
    public const string SupersededByNewerCandidate = "supersededByNewerCandidate";
    public const string RevisionConflict = "revisionConflict";
}
