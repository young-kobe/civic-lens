using System.Text;

namespace CivicLens.Core.Actions;

/// <summary>The one owner of how a GPO double quote mark reads in plain text. GPO prints it as two grave accents (``) or two apostrophes ('').
/// <see cref="Normalize"/> turns each pair into one plain double quote. The draft step and the approval check both call it, so they agree on the
/// plain form of a quote while the stored text keeps the exact characters.</summary>
public static class GpoQuoteMarks
{
    private const string PlainQuote = "\"";

    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length;)
        {
            var length = MarkLength(text, index);
            builder.Append(length == 2 ? PlainQuote : text[index].ToString());
            index += length;
        }
        return builder.ToString();
    }

    /// <summary>The stored characters that one plain character stands for at the index: 2 for a pair of marks, otherwise 1.</summary>
    public static int MarkLength(string text, int index)
    {
        if (index + 1 >= text.Length || text[index] != text[index + 1]) return 1;
        return text[index] is '`' or '\'' ? 2 : 1;
    }
}
