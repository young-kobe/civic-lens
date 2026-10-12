namespace CivicLens.Application.Issues;

public sealed record PolicyAreaConfiguration
{
    public required string Id { get; init; }
    public required string Name { get; init; }
}
