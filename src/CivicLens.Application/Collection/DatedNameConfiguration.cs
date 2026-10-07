namespace CivicLens.Application.Collection;

public sealed record DatedNameConfiguration
{
    public required string Name { get; init; }
    public DateOnly? StartsOn { get; init; }
    public DateOnly? EndsBefore { get; init; }
}
