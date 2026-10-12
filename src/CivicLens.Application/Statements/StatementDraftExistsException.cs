namespace CivicLens.Application.Statements;

public sealed class StatementDraftExistsException(string candidateId)
    : InvalidOperationException("This statement already has a draft.")
{
    public string CandidateId { get; } = candidateId;
}
