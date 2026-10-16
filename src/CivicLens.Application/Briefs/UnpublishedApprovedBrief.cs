using CivicLens.Core.Briefs;

namespace CivicLens.Application.Briefs;

public sealed record UnpublishedApprovedBrief(BriefReview Brief, int? PublishedRevisionNumber, bool CitedRecordsChanged);
