using System.Collections.Immutable;
using CivicLens.Application.Review;

namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeAnalysisCatalog(ImmutableArray<DocumentChangeAnalysisOfficial> Officials, ImmutableArray<string> IssueIds)
{
    public static DocumentChangeAnalysisCatalog Create(ReviewCatalog catalog, IReadOnlyDictionary<string, string> officialNames)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(officialNames);
        var officials = catalog.OfficialIds.Order(StringComparer.Ordinal)
            .Select(id => new DocumentChangeAnalysisOfficial(id, officialNames.GetValueOrDefault(id) ?? id)).ToImmutableArray();
        return new(officials, [.. catalog.IssueIds.Order(StringComparer.Ordinal)]);
    }
}
