using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words and links that name the votes and bills of one issue. The issue page and the vote and bill lists
/// filtered by that issue use them, so the list header repeats the heading of the issue page section that links to it.</summary>
public static class IssueRecordWords
{
    public static string VotesHeading(string issue) => $"Roll call votes on {issue}";

    public static string BillsHeading(string issue) => $"Bills tagged {issue}";

    /// <summary>The link text to the full vote list of the issue. The list holds every kind of vote and every Congress, so the text names no count.</summary>
    public static string AllVotesLink(string issue) => $"See all roll call votes tagged {issue}";

    public const string AllVotesScope = ", of every kind and Congress, newest first.";

    public static string AllBillsLink(int bills) => $"See all {PublishedWording.Count(bills)} bills";

    public const string AllBillsScope = ", newest first.";

    /// <summary>The public path of the list of one kind, filtered by the issue, without the site root. The list filter owns the query string.</summary>
    public static string ListPath(PublishedRecordKind kind, string issueId) =>
        PublicationProtocol.ListPagePath(kind) + RecordListFilter.Of(null, null, ("issue", issueId)).Query();
}
