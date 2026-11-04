namespace CivicLens.Infrastructure.Review;

internal interface IRevisionRowPage<TRow>
{
    IReadOnlyList<TRow> Rows { get; }
}
