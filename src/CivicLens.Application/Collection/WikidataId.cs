using System.Text.RegularExpressions;

namespace CivicLens.Application.Collection;

internal static partial class WikidataId
{
    public static bool IsValid(string value) => Pattern().IsMatch(value);

    [GeneratedRegex(@"^Q[1-9][0-9]*\z")]
    private static partial Regex Pattern();
}
