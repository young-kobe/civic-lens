using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

/// <summary>The current review state of a published record. The actor is set only for an approved revision that the release does not hold yet.</summary>
public sealed record ReviewedRecord(ReviewStatus ReviewStatus, int CurrentRevisionNumber, string? UnpublishedApprovalActor);
