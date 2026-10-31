using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review;

internal sealed record QueueDraft(string Headline, ReviewStatus Status, int OpenConcerns, string? HeldReason);
