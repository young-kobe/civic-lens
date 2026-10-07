namespace CivicLens.Core.Registry;

/// <summary>Represents calendar-date coverage with an inclusive start and exclusive end.</summary>
public sealed record DateRange
{
    public DateRange(DateOnly? startsOn, DateOnly? endsBefore)
    {
        if (startsOn is not null && endsBefore is not null && endsBefore <= startsOn)
            throw new ArgumentException("The exclusive end must be after the inclusive start.", nameof(endsBefore));

        StartsOn = startsOn;
        EndsBefore = endsBefore;
    }

    public DateOnly? StartsOn { get; }
    public DateOnly? EndsBefore { get; }

    public bool Contains(DateOnly date) =>
        (StartsOn is null || date >= StartsOn.Value) &&
        (EndsBefore is null || date < EndsBefore.Value);

    public bool Overlaps(DateRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return (EndsBefore is null || other.StartsOn is null || other.StartsOn.Value < EndsBefore.Value) &&
            (other.EndsBefore is null || StartsOn is null || StartsOn.Value < other.EndsBefore.Value);
    }
}
