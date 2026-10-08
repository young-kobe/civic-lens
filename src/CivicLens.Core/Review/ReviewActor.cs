namespace CivicLens.Core.Review;

public enum ReviewRole { Owner, Reviewer }

/// <summary>An authenticated subject and its explicitly granted editorial role.</summary>
public sealed record ReviewActor(string Subject, ReviewRole Role)
{
    public bool CanReview => Role is ReviewRole.Owner or ReviewRole.Reviewer;
}
