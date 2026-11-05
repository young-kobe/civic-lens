using System.Globalization;
using CivicLens.Application.Analysis;

namespace CivicLens.Application.Actions;

/// <summary>The one owner of the reading grade of a piece of English text, such as a model sentence or a headline. The grade is the Flesch-Kincaid grade level,
/// 0.39 * (words / sentences) + 11.8 * (syllables / words) - 15.59. <see cref="SentenceEnds"/> owns where a sentence ends, so a full stop in
/// "H.R." or "U.S." does not end one. A word is a run of characters between spaces. A number is one syllable. A word of letters has its groups of
/// vowels (a, e, i, o, u, y), less one for a final silent e (a final "le" keeps it), for a final "es" and for a final "ed". A final "es" is silent
/// unless the letter before it is s, x, z, c or g, or the word ends in "ches" or "shes". A final "ed" is silent unless the letter before it is t, d or
/// a vowel (a vowel before "ed" already sits in a vowel group, as in "agreed"), or the "ed" follows an "l" that a consonant other than "l" precedes. That
/// "led" is its own syllable, as in "settled", while "called" keeps a silent "ed". A word has at least one syllable.</summary>
public static class ReadingGrade
{
    /// <summary>Text above this grade goes back to the model.</summary>
    public const double MaximumSentenceGrade = 8;

    public static double Of(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;
        var syllables = words.Sum(Syllables);
        return 0.39 * words.Length / Sentences(text) + 11.8 * syllables / words.Length - 15.59;
    }

    /// <summary>The grade to one decimal. The limit check and the message both use this value, so a grade that prints as the limit never fails it.</summary>
    public static double Rounded(string text) => Math.Round(Of(text), 1, MidpointRounding.AwayFromZero);

    public static string Format(double grade) => grade.ToString("0.0", CultureInfo.InvariantCulture);

    private static int Sentences(string text)
    {
        var trimmed = text.Trim();
        return SentenceEnds.After(trimmed).Count(end => end < trimmed.Length) + 1;
    }

    private static int Syllables(string word)
    {
        var letters = new string([.. word.ToLowerInvariant().Where(char.IsLetter)]);
        var groups = 0;
        for (var index = 0; index < letters.Length; index++)
            if (IsVowel(letters[index]) && (index == 0 || !IsVowel(letters[index - 1]))) groups++;
        return Math.Max(1, HasSilentEnding(letters) && groups > 1 ? groups - 1 : groups);
    }

    private static bool HasSilentEnding(string letters) =>
        letters.Length > 2 && (SilentE(letters) || SilentEs(letters) || SilentEd(letters));

    private static bool SilentE(string letters) => letters[^1] == 'e' && !IsVowel(letters[^2]) && letters[^2] != 'l';

    private static bool SilentEs(string letters) =>
        letters.EndsWith("es", StringComparison.Ordinal) && letters[^3] is not ('s' or 'x' or 'z' or 'c' or 'g')
        && !letters.EndsWith("ches", StringComparison.Ordinal) && !letters.EndsWith("shes", StringComparison.Ordinal);

    private static bool SilentEd(string letters) =>
        letters.EndsWith("ed", StringComparison.Ordinal) && letters[^3] is not ('t' or 'd') && !IsVowel(letters[^3]) && !IsSyllabicLed(letters);

    private static bool IsSyllabicLed(string letters) => letters.Length > 3 && letters[^3] == 'l' && !IsVowel(letters[^4]) && letters[^4] != 'l';

    private static bool IsVowel(char letter) => letter is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
}
