namespace CivicLens.PublicWeb.Publication;

public sealed record HomePageData(int Release, HomeFeaturedVoteData? FeaturedVote, HomeVoteData[] Votes, HomeBriefData? Brief);
