using System.Globalization;

namespace CivicLens.Publication.Contracts;

/// <summary>
/// The one owner of the words around an AI summary of an executive action text: the label and the cut note. The Host and the public API read these
/// words. A page never keeps its own copy. The migration that makes <c>public_api.executive_actions.label</c> holds a copy of <see cref="Label"/>,
/// because SQL cannot read this class. A test compares them. <see cref="WindowLength"/> repeats the text window of the collection feature,
/// because this project has no dependency. A test compares the two.
/// </summary>
public static class ExecutiveSummaryWords
{
    public const string Label = "AI summary of the document text. An AI model wrote this. Code checked that each sentence quotes the text.";

    public const int WindowLength = 60_000;

    public static readonly string CutNote =
        $"The AI read only the first {WindowLength.ToString("N0", CultureInfo.InvariantCulture)} characters of this document. The rest is not summarized.";
}
