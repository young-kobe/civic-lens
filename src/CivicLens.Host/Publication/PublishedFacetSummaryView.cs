using CivicLens.Application.Publication;

namespace CivicLens.Host.Publication;

internal static class PublishedFacetSummaryView
{
    public static string Title(FacetKind kind) => kind == FacetKind.Issue
        ? "Votes and bills tagged with this issue"
        : "Bills tagged with this policy area";

    public static string Lede(FacetKind kind) => kind == FacetKind.Issue
        ? "Code counts the votes, bills, and resolutions on this site that are tagged with this issue. A tag says that a record is about the issue. It does not say if the record supports or opposes it. An AI model picks the issue tag of a bill or vote, and no person checks it, so a tag can be wrong. Some older records list every issue that could fit. The counts cover every page of the list below."
        : "Code counts the bills and resolutions on this site that are tagged with this policy area. A tag says that a record is about the policy area. It does not say if the record supports or opposes it. The counts cover every page of the list below.";

    public const string IssueTagsLinkText = "Where the issue tags come from";

    public const string IssueTagsAnchor = "#issue-tags";

    public static string Votes(FacetSummary summary) => summary.Votes switch
    {
        0 => "No roll call vote on this site is tagged with this issue.",
        1 => "1 roll call vote on this site is tagged with this issue.",
        var votes => $"{votes} roll call votes on this site are tagged with this issue."
    };

    public static string SplitVotes(FacetSummary summary) =>
        $"{summary.SplitVotes} of them {(summary.SplitVotes == 1 ? "was a split vote" : "were split votes")}. {PublishedOfficialView.SplitVoteMeaning}";

    public static string PartiesAgreedVotes(FacetSummary summary) =>
        $"{summary.PartiesAgreedVotes} of them {(summary.PartiesAgreedVotes == 1 ? "was a vote" : "were votes")} where both parties agreed. {PublishedOfficialView.PartiesAgreedMeaning}";

    public static string OtherVotes(FacetSummary summary) =>
        $"{summary.OtherVotes} of them {(summary.OtherVotes == 1 ? "is" : "are")} in neither group. Such a vote is procedural, is a vote for a candidate, has no Yea and Nay counts, has a party with no majority, or has no party row for the Democrats or the Republicans.";

    public const string OtherVotesLinkText = "How the vote badges are chosen";

    public const string OtherVotesAnchor = "#vote-badges";

    public const string PartiesNote = "That the parties voted the same way or on opposite sides is about the question of the vote. It does not show a position on the issue.";

    public static string Bills(FacetSummary summary, string kindLabel) => summary.Bills switch
    {
        0 => $"No bill or resolution on this site is tagged with this {kindLabel}.",
        1 => $"1 bill or resolution on this site is tagged with this {kindLabel}.",
        var bills => $"{bills} bills and resolutions on this site are tagged with this {kindLabel}."
    };
}
