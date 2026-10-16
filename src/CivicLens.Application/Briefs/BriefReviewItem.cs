using CivicLens.Core.Briefs;

namespace CivicLens.Application.Briefs;

public sealed record BriefReviewItem(BriefReview Review, bool CitedRecordsChanged);
