using System.Collections.Immutable;

namespace CivicLens.Core.Documents;

public static class DocumentLines
{
    public static ImmutableArray<DocumentLine> Split(string text, int maximumLines = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(text);
        var lines = ImmutableArray.CreateBuilder<DocumentLine>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
            if (text[index] == '\n')
            {
                lines.Add(new(start, index + 1 - start, text.Substring(start, index + 1 - start)));
                start = index + 1;
                if (lines.Count > maximumLines) return lines.ToImmutable();
            }
        if (start < text.Length || text.Length == 0)
            lines.Add(new(start, text.Length - start, text[start..]));
        return lines.ToImmutable();
    }
}
