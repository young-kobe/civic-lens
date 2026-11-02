using System.Collections.Immutable;

namespace CivicLens.Application.Briefs;

internal sealed record BriefCheckOutput(string Recommendation, ImmutableArray<BriefCheckOutputFlag> Flags);

internal sealed record BriefCheckOutputFlag(int ParagraphIndex, string Code, string? CitationId, string Note);
