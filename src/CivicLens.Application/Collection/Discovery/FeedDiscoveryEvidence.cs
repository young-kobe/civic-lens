using System.Collections.Immutable;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Discovery;

/// <summary>Immutable interpretation of a captured feed, bound to its original collection scope.</summary>
public sealed class FeedDiscoveryEvidence
{
    public FeedDiscoveryEvidence(CollectionRequest request, FeedDiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(discovery);
        request.Validate();
        if (request.Mode != CollectionMode.Feed)
            throw new ArgumentException("Discovery evidence requires a feed request.");
        discovery.ValidateAgainst(request);
        Request = request;
        Status = discovery.Status;
        Urls = discovery.Urls.ToImmutableArray();
    }

    public CollectionRequest Request { get; }
    public FeedDiscoveryStatus Status { get; }
    public ImmutableArray<string> Urls { get; }

    public FeedDiscoveryResult ToResult() => new() { Status = Status, Urls = Urls.ToArray() };

    public bool Matches(FeedDiscoveryEvidence? other) => other is not null && Request == other.Request &&
        Status == other.Status && Urls.SequenceEqual(other.Urls, StringComparer.Ordinal);
}
