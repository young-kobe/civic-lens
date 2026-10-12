namespace CivicLens.Core.Documents;

public enum UniqueQuoteResult { Found, Missing, Ambiguous, SplitsCharacter }

public static class UniqueQuote
{
    public static UniqueQuoteResult Find(string text, string quote, out int start)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrEmpty(quote);
        start = text.IndexOf(quote, StringComparison.Ordinal);
        if (start < 0) return UniqueQuoteResult.Missing;
        if (text.IndexOf(quote, start + 1, StringComparison.Ordinal) >= 0) return UniqueQuoteResult.Ambiguous;
        return char.IsLowSurrogate(quote[0]) || char.IsHighSurrogate(quote[^1]) ? UniqueQuoteResult.SplitsCharacter : UniqueQuoteResult.Found;
    }
}
