namespace CivicLens.PublicApi.Home;

/// <summary>The facts of the home card. <c>FeaturedVote</c> is the latest passage vote on a bill, or null. <c>Votes</c> holds up to 2 recent substantive votes with counts per chamber, excluding the featured vote.
/// <c>Brief</c> is null when the release holds no brief. <c>ExecutiveActions</c> has one row for each kind with active records.
/// <c>Issues</c> counts the configured issues in the Congress of the release publication day.</summary>
public sealed record HomeAnswer(int Release, HomeFeaturedVote? FeaturedVote, HomeVote[] Votes, HomeBrief? Brief)
{
    public HomeExecutiveGroup[] ExecutiveActions { get; init; } = [];
    public HomeIssue[] Issues { get; init; } = [];
}
