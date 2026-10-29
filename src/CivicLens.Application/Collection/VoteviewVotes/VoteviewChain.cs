namespace CivicLens.Application.Collection.VoteviewVotes;

/// <summary>The three jobs of one Voteview check of one Congress. The votes job renders the roll calls from all three captures.</summary>
public sealed record VoteviewChain(int Congress, string RollCallsJobId, string MembersJobId, string VotesJobId);

/// <summary>The three capture hashes and the extraction version a new check would render with.</summary>
public sealed record VoteviewBodies(string RollCallsSha256, string MembersSha256, string VotesSha256, string ExtractionVersion);
