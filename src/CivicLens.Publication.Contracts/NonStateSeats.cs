namespace CivicLens.Publication.Contracts;

/// <summary>The one owner of the six non-state codes, their names, and the title of their House member.</summary>
public static class NonStateSeats
{
    private const string Delegate = "Delegate";

    public static IReadOnlyList<NonStateSeat> All { get; } =
    [
        new("DC", "District of Columbia", Delegate),
        new("PR", "Puerto Rico", "Resident Commissioner"),
        new("GU", "Guam", Delegate),
        new("VI", "U.S. Virgin Islands", Delegate),
        new("AS", "American Samoa", Delegate),
        new("MP", "Northern Mariana Islands", Delegate)
    ];

    public static bool Contains(string code) => All.Any(seat => seat.Code == code);
}
