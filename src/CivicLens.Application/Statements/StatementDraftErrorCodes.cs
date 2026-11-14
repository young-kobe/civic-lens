namespace CivicLens.Application.Statements;

public static class StatementDraftErrorCodes
{
    public const string EvidenceUnavailable = StatementGateErrorCodes.EvidenceUnavailable;
    public const string SpeakersUnavailable = "speakersUnavailable";
    public const string HumanDraftExists = "humanDraftExists";
    public const string DraftDiscarded = "draftDiscarded";
    public const string OverByteCap = StatementGateReasons.OverByteCap;

    /// <summary>The reason of the Bypassed run of a short release, whose revision holds its source text.</summary>
    public const string SourceText = "source-text";
}
