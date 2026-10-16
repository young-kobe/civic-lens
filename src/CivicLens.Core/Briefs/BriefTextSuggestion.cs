using System.Collections.Immutable;

namespace CivicLens.Core.Briefs;

public sealed record BriefTextSuggestion(string Headline, ImmutableArray<BriefParagraph> Paragraphs, ImmutableArray<BriefCitation> Citations,
    string RunId, string Fingerprint)
{
    public bool Equals(BriefTextSuggestion? other) => other is not null && Headline == other.Headline &&
        Paragraphs.SequenceEqual(other.Paragraphs) && Citations.SequenceEqual(other.Citations) && RunId == other.RunId &&
        Fingerprint == other.Fingerprint;

    public override int GetHashCode() => HashCode.Combine(Headline, Paragraphs.Length, Citations.Length, RunId, Fingerprint);
}
