namespace CivicLens.Publication.Contracts;

public sealed record PublishedQuotation
{
    public required string ExtractionId { get; init; }
    public required int Start { get; init; }
    public required int Length { get; init; }
    public required string Quote { get; init; }
    public required PublishedQuotationKind Kind { get; init; }
}
