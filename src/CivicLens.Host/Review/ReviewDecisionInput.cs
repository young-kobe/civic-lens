namespace CivicLens.Host.Review;

public sealed class ReviewDecisionInput
{
    public string? Note { get; set; }
    public string? CorrectionNote { get; set; }
    public string[]? ResolvedDecisionIds { get; set; } = [];
}
