namespace CivicLens.PublicApi.Home;

/// <summary>The facts of the home card. <c>Votes</c> holds the newest substantive votes with counts: at most 2 of the House and 2 of the Senate, newest first.
/// <c>Brief</c> is null when the release holds no brief.</summary>
public sealed record HomeAnswer(int Release, HomeVote[] Votes, HomeBrief? Brief);
