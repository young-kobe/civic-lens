namespace CivicLens.Infrastructure.Review.Persistence;

internal interface IHeadlineSummaryRow
{
    string? Headline { get; set; }
    string? Summary { get; set; }
}
