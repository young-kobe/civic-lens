namespace CivicLens.Application.Publication;

/// <summary>The action record that a release links from a vote. The vote page and the vote list share this link.</summary>
public sealed record ReleaseVoteBill(string VoteRecordId, string BillActionRecordId);
