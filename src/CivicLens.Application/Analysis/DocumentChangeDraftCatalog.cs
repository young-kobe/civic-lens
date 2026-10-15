using System.Collections.Immutable;
using CivicLens.Application.Review;

namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeDraftCatalog(ImmutableArray<AnalysisOfficial> Officials, ImmutableArray<string> IssueIds)
{
    public static DocumentChangeDraftCatalog Create(ReviewCatalog catalog, IReadOnlyDictionary<string, string> officialNames)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(officialNames);
        var officials = catalog.OfficialIds.Order(StringComparer.Ordinal)
            .Select(id => new AnalysisOfficial(id, officialNames.GetValueOrDefault(id) ?? id)).ToImmutableArray();
        return new(officials, [.. catalog.IssueIds.Order(StringComparer.Ordinal)]);
    }
}
