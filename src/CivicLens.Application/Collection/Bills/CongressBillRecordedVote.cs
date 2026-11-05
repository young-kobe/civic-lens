using CivicLens.Core.Votes;

namespace CivicLens.Application.Collection.Bills;

public sealed record CongressBillRecordedVote(
    Chamber Chamber,
    int Congress,
    int SessionNumber,
    int RollNumber,
    DateTimeOffset? VotedAt,
    string? Url);
