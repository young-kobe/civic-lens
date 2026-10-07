using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

public interface IDocumentExtractionStore
{
    Task<DocumentExtraction?> GetAsync(string extractionId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically returns the retained record for an equal replay and rejects conflicting content for the same ID.
    /// Requires source provenance equal to an already imported captured attempt; failures leave no partial record.
    /// </summary>
    Task<DocumentExtraction> SaveAsync(DocumentExtraction extraction, CancellationToken cancellationToken);
}
