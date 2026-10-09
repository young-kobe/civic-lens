namespace CivicLens.Application.Collection.Processing;

public sealed record EvidenceProcessingClaim(EvidenceProcessingRecord Record, string LeaseToken, long Fence);
