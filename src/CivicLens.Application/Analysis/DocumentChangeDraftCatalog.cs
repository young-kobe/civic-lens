using System.Collections.Immutable;

namespace CivicLens.Application.Analysis;

public sealed record DocumentChangeDraftCatalog(ImmutableArray<AnalysisOfficial> Officials, ImmutableArray<string> IssueIds);
