namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionSpeakerRow : IStatementRevisionChildRow
{
    public string CandidateId { get; set; } = string.Empty;
    public int RevisionNumber { get; set; }
    public string OfficialId { get; set; } = string.Empty;
    public short Position { get; set; }
}
