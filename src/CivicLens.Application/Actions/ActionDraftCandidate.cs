namespace CivicLens.Application.Actions;

public sealed record ActionDraftCandidate(string CandidateId, string RecordId, string SourceId, int Congress, string BillType,
    int BillNumber, DateTimeOffset UpdateDate, bool IncludesSummaries, bool IncludesSubjects);
