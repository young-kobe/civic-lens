using System.Collections.Immutable;

namespace CivicLens.Core.Briefs;

public sealed record BriefTextSuggestion(string Headline, ImmutableArray<BriefParagraph> Paragraphs, ImmutableArray<BriefCitation> Citations,
    string RunId, string Fingerprint);
