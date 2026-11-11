namespace CivicLens.PublicApi.Issues;

/// <summary>The bills of one issue whose sponsor held <c>Party</c> on the sponsorship date. The label is the term party, so an independent has the party label of their own.</summary>
public sealed record IssuePartyCount(string Party, int Bills);
