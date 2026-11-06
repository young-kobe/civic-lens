namespace CivicLens.Publication.Contracts;

/// <summary>The one owner of public wording that the Host pages and the public API both show. A page never keeps its own copy. A view repeats it only under a comparison test.</summary>
public static class PublishedWording
{
    /// <summary>The label of a weekly brief that a model drafted. A person checked the brief before it was published.</summary>
    public const string ModelBrief = "Drafted by an AI model from reviewed records. A person checked it before publication.";

    /// <summary>The source of a bill line, which takes the first sentence of the Congressional Research Service summary.</summary>
    public const string BillLineSource = "From the Congressional Research Service summary.";
}
