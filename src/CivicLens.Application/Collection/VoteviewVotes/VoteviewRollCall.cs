namespace CivicLens.Application.Collection.VoteviewVotes;

/// <summary>One row of the Voteview roll call file. <paramref name="Part"/> is the Voteview number within the Congress.</summary>
public sealed record VoteviewRollCall(
    int Part,
    SenateVotes.SenateVoteRoll Roll,
    DateOnly Date,
    string Question,
    string? Title,
    string Result,
    string? MajorityRequirement,
    string? BillType,
    int? BillNumber,
    int YeaCount,
    int NayCount);
