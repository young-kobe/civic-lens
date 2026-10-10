namespace CivicLens.Application.Review;

public sealed record ReviewOverview(int NewChanges, int DraftsNeedingAction, int ApprovedDrafts);
