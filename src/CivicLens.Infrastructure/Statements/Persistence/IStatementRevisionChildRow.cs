namespace CivicLens.Infrastructure.Statements.Persistence;

internal interface IStatementRevisionChildRow
{
    string CandidateId { get; }
    int RevisionNumber { get; }
}
