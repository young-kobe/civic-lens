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

    /// <summary>
    /// Registers a statement candidate for each feed item that has none, newest first, and saves the extraction of each.
    /// All items share one imported captured feed attempt. The attempt gets at most <paramref name="maximumNew"/>
    /// candidates in total, so a replay never adds more. Returns the count that this call registered.
    /// </summary>
    Task<int> SaveFeedItemsAsync(IReadOnlyList<FeedItemExtraction> newestFirst, int maximumNew, CancellationToken cancellationToken);
}
