namespace CivicLens.Infrastructure.Review;

internal sealed class RankedDraft<TDraftRow>
{
    public required TDraftRow Draft { get; init; }
    public required int Priority { get; init; }
}
