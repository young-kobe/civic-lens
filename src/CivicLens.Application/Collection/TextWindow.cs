namespace CivicLens.Application.Collection;

/// <summary>The one owner of the text window rule. Bill text and executive document text keep the first <see cref="Length"/> characters,
/// cut at the last whitespace inside the window.</summary>
public static class TextWindow
{
    public const int Length = 60_000;

    internal static string Of(string text)
    {
        if (text.Length <= Length) return text;
        for (var index = Length; index > 0; index--)
            if (char.IsWhiteSpace(text[index])) return text[..index];
        throw new InvalidDataException("Text has no whitespace inside the window.");
    }
}
