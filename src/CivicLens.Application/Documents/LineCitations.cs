using System.Collections.Immutable;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Documents;

internal static class LineCitations
{
    public static SourceSpan? Line(string extractionId, ImmutableArray<DocumentLine> lines, string prefix)
    {
        var line = lines.FirstOrDefault(item => item.Text.StartsWith(prefix, StringComparison.Ordinal));
        return line is null ? null : Span(extractionId, line);
    }

    public static IReadOnlyDictionary<string, SourceSpan> ExactLines(string extractionId, string text) =>
        DocumentLines.Split(text).DistinctBy(Content).ToDictionary(Content, line => Span(extractionId, line), StringComparer.Ordinal);

    public static SourceSpan Span(string extractionId, DocumentLine line) => new(extractionId, line.Start, Content(line).Length);

    public static string Content(DocumentLine line) => line.Text.TrimEnd('\n');
}
