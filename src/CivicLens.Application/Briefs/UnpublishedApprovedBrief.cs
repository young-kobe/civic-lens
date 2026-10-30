using CivicLens.Core.Briefs;

namespace CivicLens.Application.Briefs;

public sealed record UnpublishedApprovedBrief(BriefReview Brief, bool CitedRecordsChanged);
