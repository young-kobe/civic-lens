using System.Globalization;

namespace CivicLens.Publication.Contracts;

/// <summary>
/// The one owner of the words around an AI summary of a bill text: the label, the heading of the why it matters part, the source line and the cut note.
/// The Host and the public API read these words. A page never keeps its own copy. The migration that makes <c>public_api.bills.label</c> holds a copy of
/// <see cref="Label"/>, because SQL cannot read this class. A test compares them.
/// </summary>
public static class BillTextWords
{
    public const string Label = "AI summary of the bill text. An AI model wrote this. Code checked that each sentence quotes the text.";

    public const string WhyItMattersHeading = "Why it matters, as the text states it.";

    /// <summary>The version and its date, and the link to the whole text. The caller formats the date, so the page keeps one date format. A version with no date shows without one.</summary>
    public static string SourceLine(string versionName, string? date) =>
        $"Based on: {(date is null ? versionName : $"{versionName}, {date}")}. Read the full text on Congress.gov.";

    /// <summary>The note for a text that the model read in part, or null when it read all of it. It counts characters of the one bill text version.</summary>
    public static string? CutNote(int charsRead, int charsTotal) => charsRead >= charsTotal ? null :
        $"This summary covers the first {Count(charsRead)} of {Count(charsTotal)} characters. Later sections are not covered.";

    private static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
