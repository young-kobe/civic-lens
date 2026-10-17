namespace CivicLens.Application.Statements;

public sealed record GateLabelQueueItem(string CandidateId, string SourceId, string Url, bool HeldOut);
