using System.Collections.Immutable;
using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipEditOptions(ImmutableArray<StatementPassage> Quotes, ImmutableArray<RelationshipFact> Facts)
{
    public static RelationshipEditOptions For(RelationshipRevision current, ApprovedStatement statement, RelationshipTarget target)
    {
        var saved = new StatementPassage(current.StatementQuote, statement.ArticleText.Substring(current.StatementQuote.Start, current.StatementQuote.Length));
        var covering = statement.Passages().Where(passage => passage.Span.Covers(current.Match.Span) && passage.Span != saved.Span);
        return new([saved, .. covering], target.Facts);
    }

    public void Require(RelationshipRevision revision)
    {
        if (!Quotes.Any(quote => quote.Span == revision.StatementQuote))
            throw new ArgumentException("The quote must be a passage that the statement offers.", nameof(revision));
        if (!Facts.Any(fact => fact.Id == revision.TargetFactId))
            throw new ArgumentException("The fact must be a fact that the target lists.", nameof(revision));
    }
}
