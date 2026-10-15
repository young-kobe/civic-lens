using System.Collections.Immutable;
using CivicLens.Application.Issues;

namespace CivicLens.Application.Publication;

public sealed class PublicationCatalog(IReadOnlyDictionary<string, string> officialNames, IssueConfiguration issues)
{
    public IssueConfiguration Issues { get; } = issues;

    public ImmutableDictionary<string, string> OfficialNames { get; } =
        officialNames.ToImmutableDictionary(StringComparer.Ordinal);
}
