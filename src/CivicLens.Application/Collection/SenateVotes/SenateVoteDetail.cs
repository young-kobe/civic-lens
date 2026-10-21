namespace CivicLens.Application.Collection.SenateVotes;

public sealed record SenateVoteDetail(
    SenateVote Vote,
    DateTimeOffset VoteDate,
    string Question,
    string QuestionText,
    string Title,
    string Result,
    string? ResultText,
    string? DocumentText,
    string? MajorityRequirement,
    string? DocumentName,
    SenateVoteCounts Counts,
    IReadOnlyList<SenateVoteMember> Members);
