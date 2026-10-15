using System.Collections.Immutable;

namespace CivicLens.Application.Publication;

public sealed class PublicationCatalog(IReadOnlyDictionary<string, string> officialNames)
{
    public ImmutableDictionary<string, string> OfficialNames { get; } =
        officialNames.ToImmutableDictionary(StringComparer.Ordinal);
}
