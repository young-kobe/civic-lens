namespace CivicLens.Application.Documents;

public sealed class DocumentHistoryLimitException : InvalidOperationException
{
    public DocumentHistoryLimitException()
        : base("Document history exceeds its observation, extraction, or text limit. No partial history was returned.")
    {
    }
}
