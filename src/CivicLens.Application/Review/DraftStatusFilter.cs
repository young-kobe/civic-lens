namespace CivicLens.Application.Review;

/// <summary>Approved is the Approved status. NeedsAction is every other status, so the two always partition All.</summary>
public enum DraftStatusFilter { All, NeedsAction, Approved }
