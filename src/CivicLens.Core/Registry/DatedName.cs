namespace CivicLens.Core.Registry;

/// <summary>A name associated with a person for a calendar-date interval.</summary>
public sealed record DatedName
{
    public DatedName(string name, DateRange range)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A name is required.", nameof(name));
        ArgumentNullException.ThrowIfNull(range);
        Name = name;
        Range = range;
    }

    public string Name { get; }
    public DateRange Range { get; }
}
