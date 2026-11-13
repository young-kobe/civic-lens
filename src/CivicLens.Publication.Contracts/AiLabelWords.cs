namespace CivicLens.Publication.Contracts;

/// <summary>
/// The one owner of the AI labels of a bill, a vote and a statement, and of the check line words that say a model wrote only the summary.
/// The Host and the public API read these words. A page never keeps its own copy. The migrations that make the <c>label</c> columns of the
/// <c>public_api</c> views hold copies of the labels, because SQL cannot read this class. Tests compare them.
/// </summary>
public static class AiLabelWords
{
    /// <summary>The label of a bill or vote summary that a model wrote.</summary>
    public const string ModelWording = "An AI summary of the official record";

    /// <summary>The label of a bill or vote summary that code wrote from the Congress.gov data.</summary>
    public const string CodeWording = "Written automatically from Congress.gov data, with no AI";

    /// <summary>The label of a statement summary that a model wrote and code approved.</summary>
    public const string StatementModelWording = "An AI summary of this press release.";

    /// <summary>The label of a statement summary that a model wrote and a person approved.</summary>
    public const string StatementCheckedByPersonWording = "Drafted by an AI model from this source. A person checked it before publication.";

    /// <summary>The end of the check line under a model summary of a statement, a document change or an executive action.</summary>
    public const string ModelWroteOnlyThisSummary = "A model wrote only this summary.";
}
