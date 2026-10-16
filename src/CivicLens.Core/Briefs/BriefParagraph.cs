using System.Collections.Immutable;

namespace CivicLens.Core.Briefs;

public sealed record BriefParagraph(string Text, ImmutableArray<string> CitationIds)
{
    public bool Equals(BriefParagraph? other) => other is not null && Text == other.Text && CitationIds.SequenceEqual(other.CitationIds);

    public override int GetHashCode() => HashCode.Combine(Text, CitationIds.Length);
}
