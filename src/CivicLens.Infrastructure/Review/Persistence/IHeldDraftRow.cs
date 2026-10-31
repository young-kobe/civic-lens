namespace CivicLens.Infrastructure.Review.Persistence;

/// <summary>A draft of a kind that code can approve. It keeps why code left the newest revision for a person.</summary>
internal interface IHeldDraftRow : IReviewDraftRow
{
    string? HeldReason { get; set; }
}
