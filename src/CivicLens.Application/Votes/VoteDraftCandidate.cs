namespace CivicLens.Application.Votes;

public sealed record VoteDraftCandidate(string CandidateId, string RecordId, string SourceId, int Congress, int SessionNumber,
    int RollCallNumber, string? BillType, int? BillNumber, DateTimeOffset UpdateDate);
