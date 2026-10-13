using CivicLens.Collection.Contracts;

namespace CivicLens.Collector.Http;

internal sealed class DiscoveryCandidates(CollectionRequest request)
{
    private const int MaximumUrlLength = 4096;
    private readonly List<string> urls = [];
    private readonly HashSet<string> seen = new(StringComparer.Ordinal);
    private readonly HashSet<string> outOfScope = new(StringComparer.Ordinal);

    public DiscoveryStatus? Admit(Uri resolved)
    {
        var candidate = request.ResolveCandidate(resolved);
        if (candidate is null)
        {
            outOfScope.Add(resolved.AbsoluteUri);
            return null;
        }
        var value = candidate.AbsoluteUri;
        if (value.Length > MaximumUrlLength) return DiscoveryStatus.Invalid;
        if (seen.Add(value)) urls.Add(value);
        return urls.Count > request.MaxCandidates ? DiscoveryStatus.LimitExceeded : null;
    }

    public DiscoveryResult Parsed() => new()
    {
        Status = DiscoveryStatus.Parsed,
        Urls = urls.ToArray(),
        OutOfScopeCount = outOfScope.Count
    };

    public DiscoveryResult Failed(DiscoveryStatus status) => new()
    {
        Status = status,
        Urls = [],
        OutOfScopeCount = 0
    };
}
