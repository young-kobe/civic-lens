namespace CivicLens.Publication.Contracts;

/// <summary>
/// The one owner of the words around an AI summary of an executive action text: the label and the cut note. The Host and the public API read these
/// words. A page never keeps its own copy. The migration that makes <c>public_api.executive_action_summaries.label</c> holds a copy of
/// <see cref="Label"/>, because SQL cannot read this class. A test compares them.
/// </summary>
public static class ExecutiveSummaryWords
{
    public const string Label = "An AI model summarized the document text. Code checks each quote.";

    public const string CutNote = "This summary covers only the first part of the document.";
}
