namespace CivicLens.Application.Votes;

public static class VoteDraftErrorCodes
{
    public const string EvidenceUnavailable = "evidenceUnavailable";
    public const string RenderedTextMismatch = "renderedTextMismatch";
    public const string ExtractionVersionChanged = "extractionVersionChanged";
    public const string OverByteCap = "overByteCap";
    public const string TooManyMembers = "tooManyMembers";
    public const string FactTooLong = "factTooLong";
    public const string NoTrackedMember = "noTrackedMember";
    public const string Unchanged = "unchanged";
    public const string MemberVotesChanged = "memberVotesChanged";
    public const string SupersededByNewerCandidate = "supersededByNewerCandidate";
    public const string RevisionConflict = "revisionConflict";
}
