using CivicLens.Core.Statements;

namespace CivicLens.Application.Statements;

public sealed record StatementSelectableSpan(StatementSpan Span, string Text, StatementSpanOrigin Origin)
{
    public string Key => KeyOf(Span);

    public static string KeyOf(StatementSpan span) => $"{span.Start}:{span.Length}";
}

public enum StatementSpanOrigin { Saved, QuotedPassage, Paragraph }
