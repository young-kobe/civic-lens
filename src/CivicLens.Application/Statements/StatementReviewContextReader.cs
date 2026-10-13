namespace CivicLens.Application.Statements;

public sealed class StatementReviewContextReader(IStatementDraftStore evidence, StatementSelectionResolver selections)
{
    public async Task<StatementReviewContext> ReadAsync(string candidateId, CancellationToken cancellationToken)
    {
        var read = await evidence.ReadEvidenceAsync(candidateId, cancellationToken)
            ?? throw new KeyNotFoundException("Statement candidate was not found.");
        var resolved = selections.Resolve(read.ConfigurationRevision, read.Article.Extraction.SourceAttempt.SourceId,
            read.Article.FirstCapturedOn);
        return new(read.Article, resolved.Allowed, resolved.Officials);
    }
}
