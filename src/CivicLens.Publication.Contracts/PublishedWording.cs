namespace CivicLens.Publication.Contracts;

/// <summary>The one owner of public wording that the Host pages and the public API both show. A page never keeps its own copy. A view repeats it only under a comparison test.</summary>
public static class PublishedWording
{
    /// <summary>The label of a weekly brief that a model drafted. A person checked the brief before it was published.</summary>
    public const string ModelBrief = "Drafted by an AI model from reviewed records. A person checked it before publication.";

    /// <summary>The label of a document change that a model drafted from source versions. A person checked it before publication.</summary>
    public const string ModelDocumentChange = "Drafted by an AI model from these source versions. A person checked it before publication.";

    public const string IssueIncludesLabel = "Includes";

    public const string IssueExcludesLabel = "Does not include";

    public const string IssueTagCaveat = "A tag says that a record is about the issue. It does not say if the record supports or opposes it. An AI model picks the issue tag of a bill or vote, and no person checks it, so a tag can be wrong.";

    /// <summary>The source of a bill line, which takes the first sentence of the Congressional Research Service summary.</summary>
    public const string BillLineSource = "From the Congressional Research Service summary.";
}
