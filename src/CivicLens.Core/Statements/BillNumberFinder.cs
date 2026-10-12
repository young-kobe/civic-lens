using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace CivicLens.Core.Statements;

public static partial class BillNumberFinder
{
    public static ImmutableArray<string> Find(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return [.. BillPattern().Matches(text).Select(Normalize).Distinct(StringComparer.Ordinal)];
    }

    private static string Normalize(Match match) =>
        Regex.Replace(match.Groups["type"].Value, @"\s", "") + " " + match.Groups["number"].Value;

    [GeneratedRegex(@"(?<![\p{L}\p{N}.])(?<type>H\.\s?R\.|H\.\s?J\.\s?Res\.|H\.\s?Con\.\s?Res\.|H\.\s?Res\.|S\.\s?J\.\s?Res\.|S\.\s?Con\.\s?Res\.|S\.\s?Res\.|S\.)\s?(?<number>[1-9]\d{0,4})(?![\p{L}\p{N}])",
        RegexOptions.CultureInvariant)]
    private static partial Regex BillPattern();
}
