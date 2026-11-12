namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>An original document with a pinned body URL, no text row, and no text job.</summary>
public sealed record FederalRegisterTextTarget(string DocumentNumber, string BodyUrl, DateOnly PublishedOn);

/// <summary>The targets of one run, and the count of originals with no text row and no pinned body URL.</summary>
public sealed record FederalRegisterTextListing(IReadOnlyList<FederalRegisterTextTarget> Targets, int Unpinned);

public interface IFederalRegisterTextBackfillStore
{
    /// <summary>Jobs of the documents source or the text source that are queued or leased.</summary>
    Task<int> CountPendingJobsAsync(ConfiguredCollectionSource textSource, CancellationToken cancellationToken);

    /// <summary>The newest original documents first. A document with a text row or a text job is not listed.</summary>
    Task<FederalRegisterTextListing> ListNewestOriginalsWithoutTextAsync(ConfiguredCollectionSource textSource, int limit,
        CancellationToken cancellationToken);

    /// <summary>Returns false when the text job of the document exists.</summary>
    Task<bool> AdmitTextJobAsync(ConfiguredCollectionSource textSource, FederalRegisterTextTarget target, CancellationToken cancellationToken);
}
