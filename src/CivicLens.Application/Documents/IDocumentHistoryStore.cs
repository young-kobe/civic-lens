namespace CivicLens.Application.Documents;

public interface IDocumentHistoryStore
{
    /// <summary>
    /// Returns every matching attempt in observed-time then ordinal attempt-ID order, rejecting rather than truncating
    /// when the requested bound is exceeded. Each 304 observation carries extractions from its exact prior capture.
    /// </summary>
    Task<IReadOnlyList<DocumentHistoryObservation>> GetAsync(string sourceId, string requestedUrl,
        int maximumObservations, CancellationToken cancellationToken);
}
