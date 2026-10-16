using System.Collections.Immutable;

namespace CivicLens.Application.Briefs;

internal sealed record BriefDraftOutput(string Headline, ImmutableArray<BriefDraftOutputParagraph> Paragraphs, double Confidence);

internal sealed record BriefDraftOutputParagraph(string Text, ImmutableArray<string> CitationIds);
