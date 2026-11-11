using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.People;

/// <summary>
/// The one owner of the person ID rule. The ID is the address of the public pages, so a later name change never changes it.
/// The ID is the official name in lowercase words joined by hyphens. A taken ID gets the bioguide ID as a suffix.
/// A name with no letters, or an ID longer than the facet limit, falls back to the lowercase bioguide ID.
/// </summary>
public static partial class PersonIdRule
{
    /// <summary>Returns an ID that is not in <paramref name="takenIds"/> and adds it to the set.</summary>
    public static string Mint(string fullName, string bioguideId, ISet<string> takenIds)
    {
        ArgumentNullException.ThrowIfNull(takenIds);
        var bioguide = bioguideId.ToLowerInvariant();
        var slug = NotLowerAlphanumeric().Replace(WithoutAccents(fullName).ToLowerInvariant(), "-").Trim('-');
        var id = Candidates(slug, bioguide)
            .FirstOrDefault(candidate => candidate.Length <= PublicationProtocol.MaximumFacetIdLength && !takenIds.Contains(candidate)) ?? bioguide;
        takenIds.Add(id);
        return id;
    }

    private static IEnumerable<string> Candidates(string slug, string bioguide)
    {
        if (slug.Length == 0) yield break;
        yield return slug;
        yield return $"{slug}-{bioguide}";
    }

    private static string WithoutAccents(string text) =>
        new(text.Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark).ToArray());

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NotLowerAlphanumeric();
}
