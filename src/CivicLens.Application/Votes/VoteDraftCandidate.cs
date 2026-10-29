using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed record VoteDraftCandidate(string CandidateId, string RecordId, string SourceId, Chamber Chamber, int Congress, int SessionNumber,
    int RollCallNumber, string? BillType, int? BillNumber, DateTimeOffset UpdateDate,
    string? RosterJobId);
