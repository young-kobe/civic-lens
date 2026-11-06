namespace CivicLens.Application.Votes;

/// <summary>The newest Congressional Research Service summary of the voted bill, as saved text, and the bill line that <c>BillLine</c> takes from it.</summary>
public sealed record VoteBillSummary(string Text, string Line);
