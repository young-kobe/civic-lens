namespace CivicLens.Application.Collection.VoteviewVotes;

/// <summary>One row of the Voteview member file for a senator. The President row has no bioguide ID and is never read.</summary>
public sealed record VoteviewMember(int Icpsr, string BioguideId, string Name, string Party, string State);
