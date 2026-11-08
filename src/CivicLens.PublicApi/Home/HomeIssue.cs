namespace CivicLens.PublicApi.Home;

/// <summary>Counts for one issue in the Congress of the active release's publication day.</summary>
public sealed record HomeIssue(string IssueId, string Name, int Congress, int SubstantiveVotes, int Bills);
