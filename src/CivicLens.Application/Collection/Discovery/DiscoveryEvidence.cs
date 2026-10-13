using System.Collections.Immutable;
using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Discovery;

/// <summary>Immutable interpretation of a captured discovery response, bound to its original collection scope.</summary>
public sealed class DiscoveryEvidence
{
    public DiscoveryEvidence(CollectionRequest request, DiscoveryResult discovery)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(discovery);
        request.ValidateReadable();
        if (request.Mode is not (CollectionMode.Feed or CollectionMode.Html))
            throw new ArgumentException("Discovery evidence requires a discovery request.");
        discovery.ValidateAgainst(request);
        Request = request;
        Status = discovery.Status;
        Urls = discovery.Urls.ToImmutableArray();
        OutOfScopeCount = discovery.OutOfScopeCount;
    }

    public CollectionRequest Request { get; }
    public DiscoveryStatus Status { get; }
    public ImmutableArray<string> Urls { get; }
    public int? OutOfScopeCount { get; }

    public DiscoveryResult ToResult() => new() { Status = Status, Urls = Urls.ToArray(), OutOfScopeCount = OutOfScopeCount };

    public bool Matches(DiscoveryEvidence? other) => other is not null && Request == other.Request &&
        Status == other.Status && OutOfScopeCount == other.OutOfScopeCount && Urls.SequenceEqual(other.Urls, StringComparer.Ordinal);
}
