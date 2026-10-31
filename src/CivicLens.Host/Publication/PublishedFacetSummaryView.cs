using CivicLens.Application.Publication;

namespace CivicLens.Host.Publication;

internal static class PublishedFacetSummaryView
{
    public static string Title(string kindLabel) => $"Votes and bills for this {kindLabel}";

    public static string Lede(string kindLabel) =>
        $"Code counts the votes and bills on this site that carry this {kindLabel} as a tag. A tag says that a record is about the {kindLabel}. It does not say if the record supports or opposes it. The counts cover every page of the list below.";

    public static string Votes(FacetSummary summary, string kindLabel) => summary.Votes == 0
        ? $"No roll call vote on this site has this {kindLabel} as a tag."
        : $"{summary.Votes} roll call {PublishedOfficialView.Noun(summary.Votes, "vote")} on this site {(summary.Votes == 1 ? "has" : "have")} this {kindLabel} as a tag.";

    public static string SplitVotes(FacetSummary summary) =>
        $"{summary.SplitVotes} of them {(summary.SplitVotes == 1 ? "was a split vote" : "were split votes")}. {PublishedOfficialView.SplitVoteMeaning}";

    public static string PartiesAgreedVotes(FacetSummary summary) =>
        $"{summary.PartiesAgreedVotes} of them {(summary.PartiesAgreedVotes == 1 ? "was a vote" : "were votes")} where both parties agreed. On such a vote, most Democrats and most Republicans who cast Yea or Nay voted the same way.";

    public static string Bills(FacetSummary summary, string kindLabel) => summary.Bills == 0
        ? $"No bill on this site has this {kindLabel} as a tag."
        : $"{summary.Bills} {PublishedOfficialView.Noun(summary.Bills, "bill")} on this site {(summary.Bills == 1 ? "has" : "have")} this {kindLabel} as a tag.";
}
