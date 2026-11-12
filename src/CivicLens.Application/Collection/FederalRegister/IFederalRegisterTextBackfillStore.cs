namespace CivicLens.Application.Collection.FederalRegister;

/// <summary>An original document with a pinned body URL, no text row, and no text job.</summary>
public sealed record FederalRegisterTextTarget(string DocumentNumber, string BodyUrl, DateOnly PublishedOn);

/// <summary>An original document with no text row, no pinned body URL, and no refetch job.</summary>
public sealed record FederalRegisterBodyUrlTarget(string DocumentNumber, DateOnly PublishedOn);

/// <summary>The targets of one run. Each list holds at most the limit. NoUrlWithRefetch counts all originals that have no body URL
/// and have a refetch job in any state.</summary>
public sealed record FederalRegisterTextListing(IReadOnlyList<FederalRegisterTextTarget> Targets, IReadOnlyList<FederalRegisterBodyUrlTarget> Unpinned,
    int NoUrlWithRefetch);

public interface IFederalRegisterTextBackfillStore
{
    /// <summary>Jobs of the documents source or the text source that are queued or leased.</summary>
    Task<int> CountPendingJobsAsync(ConfiguredCollectionSource textSource, CancellationToken cancellationToken);

    /// <summary>The newest original documents first. A document with a text row, a text job, or (when unpinned) a refetch job is not listed.</summary>
    Task<FederalRegisterTextListing> ListNewestOriginalsWithoutTextAsync(ConfiguredCollectionSource textSource, int limit,
        CancellationToken cancellationToken);

    /// <summary>Returns false when the refetch job of the document exists.</summary>
    Task<bool> AdmitBodyUrlJobAsync(ConfiguredCollectionSource textSource, FederalRegisterBodyUrlTarget target, CancellationToken cancellationToken);

    /// <summary>Returns false when the text job of the document exists.</summary>
    Task<bool> AdmitTextJobAsync(ConfiguredCollectionSource textSource, FederalRegisterTextTarget target, CancellationToken cancellationToken);
}
