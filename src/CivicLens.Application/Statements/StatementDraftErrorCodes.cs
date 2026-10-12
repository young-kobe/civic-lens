namespace CivicLens.Application.Statements;

public static class StatementDraftErrorCodes
{
    public const string EvidenceUnavailable = StatementGateErrorCodes.EvidenceUnavailable;
    public const string SpeakersUnavailable = "speakersUnavailable";
    public const string HumanDraftExists = "humanDraftExists";
    public const string DraftDiscarded = "draftDiscarded";
    public const string OverByteCap = StatementGateReasons.OverByteCap;
}
