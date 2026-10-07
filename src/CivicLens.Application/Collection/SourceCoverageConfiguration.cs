namespace CivicLens.Application.Collection;

public sealed record SourceCoverageConfiguration
{
    public required string PersonId { get; init; }
    public DateOnly? StartsOn { get; init; }
    public DateOnly? EndsBefore { get; init; }
}
