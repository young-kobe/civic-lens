using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words that name the seat of an official in a list row or card, of the House title of a seat, and of the office words of an executive office code.
/// The place words come from <see cref="OfficeLede.Place"/>, so a row, a card, search and the official page lede name a seat by one rule.</summary>
public static class OfficialSeat
{
    public static string? Text(PublishedOfficeKind? kind, string? office, string? state, int? district)
    {
        if (kind is null) return office;
        var role = kind switch
        {
            PublishedOfficeKind.Representative => HouseRole(state),
            PublishedOfficeKind.Senator => "Senator",
            PublishedOfficeKind.Executive => ExecutiveRole(office) ?? office ?? kind.ToString(),
            _ => office ?? kind.ToString()
        };
        return role + OfficeLede.Place(state, district);
    }

    /// <summary>The House title of a seat: "Delegate" or "Resident Commissioner" for a non-state code, else "Representative".</summary>
    public static string HouseRole(string? state) =>
        UsStates.HouseOfficeByNonStateCode.GetValueOrDefault(state ?? string.Empty) ?? "Representative";

    /// <summary>The office words of an executive office code of the executive roster, such as "President" for "prez". Any other code has no words.</summary>
    public static string? ExecutiveRole(string? office) => office switch
    {
        "prez" => "President",
        "viceprez" => "Vice President",
        _ => null
    };
}
