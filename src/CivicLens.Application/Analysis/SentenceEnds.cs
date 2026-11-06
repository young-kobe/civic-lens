using System.Text.RegularExpressions;

namespace CivicLens.Application.Analysis;

/// <summary>The one owner of where a sentence ends in English prose. A sentence ends at ".", "?" or "!" (with closing quotes or brackets after it)
/// before white space and a capital letter, a quote or a bracket, or at the end of the text. It does not end after an abbreviation: a token of single
/// letters with periods, such as "H.R.", "U.S." or "D.C.", or a word of <see cref="Abbreviations"/>, which holds the titles Rep., Sen., Mr., Mrs., Ms. and Dr., so a title before a name never ends a sentence.</summary>
internal static partial class SentenceEnds
{
    internal const string ClosingCharacters = "\"”’)";

    // "L." in "Pub. L." is a single letter token, so the single letter rule covers it.
    private static readonly string[] Abbreviations = ["St.", "Mr.", "Mrs.", "Ms.", "Dr.", "Rep.", "Sen.", "No.", "Sec.", "Pub."];

    /// <summary>The index after each sentence end, in order. The last one is the text length when the text ends with a sentence end.</summary>
    public static IEnumerable<int> After(string text)
    {
        foreach (Match mark in SentenceEnd().Matches(text))
        {
            var end = mark.Index + mark.Length;
            if (!EndsAbbreviation(text[..end])) yield return end;
        }
    }

    private static bool EndsAbbreviation(string sentence)
    {
        var token = sentence.Split()[^1];
        return Abbreviations.Contains(token) || InitialsToken().IsMatch(token);
    }

    [GeneratedRegex("[.!?][" + ClosingCharacters + @"]*(?=\s+[\p{Lu}""“(]|$)")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"^(\p{L}\.)+$")]
    private static partial Regex InitialsToken();
}
