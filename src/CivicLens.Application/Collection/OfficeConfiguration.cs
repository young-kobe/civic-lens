namespace CivicLens.Application.Collection;

public sealed record OfficeConfiguration
{
    public const int MaximumPerPerson = 64;

    public required string Title { get; init; }
    public required DateOnly StartsOn { get; init; }
    public DateOnly? EndsOn { get; init; }
    public string? Party { get; init; }
}
