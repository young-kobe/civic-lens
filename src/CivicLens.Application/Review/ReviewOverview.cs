namespace CivicLens.Application.Review;

/// <summary>Counts that equal the totals of the matching lists: eligible comparisons, then drafts by status filter.</summary>
public sealed record ReviewOverview(int NewChanges, int DraftsNeedingAction, int ApprovedDrafts);
