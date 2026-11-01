using System.Collections.Immutable;
using CivicLens.Core.Documents;

namespace CivicLens.Core.Review;

/// <summary>Rules that make a posted form and a stored revision comparable. A browser posts line breaks as CRLF and posts an empty
/// optional field as empty. The UI cannot reorder a chip list. The comparison uses these forms and the stored data does not change.</summary>
public static class ComparableText
{
    public static string Lines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');

    public static string? LinesOrNull(string? text) => string.IsNullOrEmpty(text) ? null : Lines(text);

    public static ImmutableArray<string> Sorted(ImmutableArray<string> values) =>
        values.IsDefault ? values : [.. values.Order(StringComparer.Ordinal)];

    public static ImmutableArray<SourceSpan> Sorted(ImmutableArray<SourceSpan> spans) =>
        spans.IsDefault ? spans : [.. spans.OrderBy(span => span.ExtractionId, StringComparer.Ordinal).ThenBy(span => span.Start).ThenBy(span => span.Length)];
}
