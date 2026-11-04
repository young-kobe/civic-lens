namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionOtherSpeakerRow : IStatementRevisionChildRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public short Position { get; set; }
    public string SpeakerName { get; set; } = string.Empty;
}
