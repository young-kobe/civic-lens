using System.Text.RegularExpressions;

namespace CivicLens.Core.Actions;

/// <summary>
/// The definitions section of a bill text, found by code and never by the model. It is the first section whose capital-letter heading holds the word
/// DEFINITION or DEFINITIONS, such as "SEC. 2. DEFINITIONS.". It ends where the next section or title heading starts, or at the end of the text.
/// Start and End are offsets in the body, the text after the header lines. A bill with no such heading has no definitions section and no key terms.
/// The one owner of this rule is <see cref="Find"/>, which both the draft step and the approval check call on the same stored body.
/// </summary>
public sealed partial record BillDefinitionsSection(int Start, int End, string Heading, string? NextHeading)
{
    public static BillDefinitionsSection? Find(string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        foreach (Match heading in SectionHeading().Matches(body))
        {
            if (!DefinitionWord().IsMatch(heading.Groups["title"].Value)) continue;
            var next = NextBoundary(body, heading.Index + heading.Length);
            return new(heading.Index, next?.Index ?? body.Length, heading.Value, next?.Value);
        }
        return null;
    }

    public bool Contains(int start, int length) => start >= Start && start + length <= End;

    private static Match? NextBoundary(string body, int from)
    {
        var section = SectionHeading().Match(body, from);
        var title = TitleHeading().Match(body, from);
        if (!section.Success) return title.Success ? title : null;
        return title.Success && title.Index < section.Index ? title : section;
    }

    [GeneratedRegex(@"\bSEC\.\s+\d+[A-Z]?\.\s+(?<title>[A-Z][A-Z0-9 ,;:'\u2019&()/\u2014\u2013-]*?)\.(?=\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"\bTITLE\s+[IVXLC]+\s*[\u2014\u2013-]", RegexOptions.CultureInvariant)]
    private static partial Regex TitleHeading();

    [GeneratedRegex(@"\bDEFINITIONS?\b", RegexOptions.CultureInvariant)]
    private static partial Regex DefinitionWord();
}
