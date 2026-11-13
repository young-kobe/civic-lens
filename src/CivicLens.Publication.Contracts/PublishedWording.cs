using System.Globalization;

namespace CivicLens.Publication.Contracts;

/// <summary>The one owner of public wording that the Host pages and the public API both show. A page never keeps its own copy. A view repeats it only under a comparison test.</summary>
public static class PublishedWording
{
    /// <summary>The label of a weekly brief that a model drafted. A person checked the brief before it was published.</summary>
    public const string ModelBrief = "Drafted by an AI model from reviewed records. A person checked it before publication.";

    /// <summary>The label of a document change that a model drafted from source versions. A person checked it before publication.</summary>
    public const string ModelDocumentChange = "Drafted by an AI model from these source versions. A person checked it before publication.";

    /// <summary>The label of a relationship reason that a model drafted from the statement and the record. A person checked it before publication.</summary>
    public const string ModelRelationship = "Drafted by an AI model from these two records. A person checked it before publication.";

    public const string IssueIncludesLabel = "Includes";

    public const string IssueExcludesLabel = "Does not include";

    public const string IssueTagCaveat = "A tag says that a record is about the issue. It does not say if the record supports or opposes it. An AI model picks the issue tag of a bill or vote, and no person checks it, so a tag can be wrong.";

    /// <summary>The source of a bill line, which takes the first sentence of the Congressional Research Service summary.</summary>
    public const string BillLineSource = "From the Congressional Research Service summary.";

    /// <summary>The split vote line of an issue page, as the text before the glossary term and the term itself. One split vote reads "was a split vote". Any other count reads "were split votes".</summary>
    public static (string Lead, string Term) SplitVoteLine(int splitVotes, int substantiveVotes)
    {
        var votes = substantiveVotes == 1 ? "that 1 vote" : $"those {substantiveVotes} votes";
        return splitVotes == 1 || substantiveVotes == 1
            ? ($"{splitVotes} of {votes} was a ", "split vote")
            : ($"{splitVotes} of {votes} were ", "split votes");
    }

    /// <summary>The one number format of public text: whole numbers with a comma between groups of three digits, such as 1,234.</summary>
    public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
