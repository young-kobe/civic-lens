using System.Text.RegularExpressions;

namespace CivicLens.Application.Collection;

internal static partial class BioguideId
{
    public static bool IsValid(string value) => Pattern().IsMatch(value);

    [GeneratedRegex(@"^[A-Z][0-9]{6}\z")]
    private static partial Regex Pattern();
}
