using CivicLens.Core.Documents;

namespace CivicLens.Application.Statements;

public sealed record StatementSelectableSpan(SourceSpan Span, string Text, StatementSpanOrigin Origin)
{
    public string Key => KeyOf(Span);

    public static string KeyOf(SourceSpan span) => $"{span.Start}:{span.Length}";
}

public enum StatementSpanOrigin { Saved, QuotedPassage, Paragraph }
