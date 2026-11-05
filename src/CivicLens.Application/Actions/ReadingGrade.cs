using System.Globalization;

namespace CivicLens.Application.Actions;

/// <summary>The one owner of the reading grade of a model sentence. The grade is the Flesch-Kincaid grade level,
/// 0.39 * (words / sentences) + 11.8 * (syllables / words) - 15.59. Each model sentence counts as one sentence, because the output schema
/// gives one sentence for each item, and a full stop in "H.R." or "U.S." is not a sentence end. A word is a run of characters between
/// spaces. Its syllables are its groups of vowels (a, e, i, o, u, y), less one for a final silent e (a final "le" keeps it). A word has at least one syllable.</summary>
public static class ReadingGrade
{
    /// <summary>A sentence above this grade goes back to the model.</summary>
    public const double MaximumSentenceGrade = 8;

    public static double Of(string sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        var words = sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return 0;
        var syllables = words.Sum(Syllables);
        return 0.39 * words.Length + 11.8 * syllables / words.Length - 15.59;
    }

    public static string Format(double grade) => grade.ToString("0.0", CultureInfo.InvariantCulture);

    private static int Syllables(string word)
    {
        var letters = new string([.. word.ToLowerInvariant().Where(char.IsLetter)]);
        var groups = 0;
        for (var index = 0; index < letters.Length; index++)
            if (IsVowel(letters[index]) && (index == 0 || !IsVowel(letters[index - 1]))) groups++;
        var silentE = letters.Length > 2 && letters[^1] == 'e' && !IsVowel(letters[^2]) && letters[^2] != 'l' && groups > 1;
        return Math.Max(1, silentE ? groups - 1 : groups);
    }

    private static bool IsVowel(char letter) => letter is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
}
