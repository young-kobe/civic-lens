namespace CivicLens.PublicWeb.Publication;

public sealed record HomePageData(int Release, HomeFeaturedVoteData? FeaturedVote, HomeVoteData[] Votes, HomeBriefData? Brief)
{
    public HomeExecutiveGroupData[] ExecutiveActions { get; init; } = [];
    public HomeIssueData[] Issues { get; init; } = [];
    /// <summary>The page data of the featured vote, from the release of this home answer. Its members draw the map and its party counts draw the party bars. Null when no such read exists.</summary>
    public VotePageData? FeaturedVoteDetail { get; init; }
    /// <summary>The four party boards of the release of this home answer. Empty when no such read exists.</summary>
    public IReadOnlyList<BoardPanelData> Boards { get; init; } = [];
}
