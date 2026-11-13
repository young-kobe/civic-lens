namespace CivicLens.Publication.Contracts;

/// <summary>
/// The one owner of the words around an AI summary of a bill text: the label, the heading of the why it matters part, the source line, the cut note,
/// the label of the key terms, and the cut note of a key term.
/// The Host and the public API read these words. A page never keeps its own copy. The migration that makes <c>public_api.bills.label</c> holds a copy of
/// <see cref="Label"/>, because SQL cannot read this class. A test compares them.
/// </summary>
public static class BillTextWords
{
    public const string Label = "AI summary of the bill text. An AI model wrote this. Each sentence comes with an exact quote from the text. Code checked the quotes, not the meaning of the sentences.";

    /// <summary>The owner approved label of the key terms. The view column <c>public_api.bill_key_terms.label</c> serves it, and the page reads that
    /// column. The migrations <c>ActionRevisionKeyTerms</c> and <c>PublicApiBillTextQuotes</c> hold copies, because SQL cannot read this class. A test compares them.</summary>
    public const string KeyTermsLabel = "Key terms chosen by an AI model. Each definition is an exact quote from the definitions section of the bill. Code checked the quotes, not the choice of terms.";

    public const string WhyItMattersHeading = "Why it matters, as the text states it.";

    /// <summary>The note under a key term quote that ends before the end of its definition paragraph. The owner set this text. The page shows
    /// <see cref="KeyTermCutNote"/>, then <see cref="KeyTermCutLink"/> as the link to the bill on Congress.gov.</summary>
    public const string KeyTermCutNote = "This quote is only the start of the definition.";

    public const string KeyTermCutLink = "Read the rest on Congress.gov.";

    /// <summary>The version and its date, and the link to the whole text. The caller formats the date, so the page keeps one date format. A version with no date shows without one.</summary>
    public static string SourceLine(string versionName, string? date) =>
        $"Based on: {(date is null ? versionName : $"{versionName}, {date}")}. Read the full text on Congress.gov.";

    /// <summary>The note for a text that the model read in part, or null when it read all of it. It counts characters of the one bill text version.</summary>
    public static string? CutNote(int charsRead, int charsTotal) => charsRead >= charsTotal ? null :
        $"This summary covers the first {PublishedWording.Count(charsRead)} of {PublishedWording.Count(charsTotal)} characters. Later sections are not covered.";
}
