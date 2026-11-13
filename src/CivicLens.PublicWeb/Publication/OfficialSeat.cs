using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words that name the seat of an official in a list row, and of the office words of an executive office code.</summary>
public static class OfficialSeat
{
    public static string? Text(PublishedOfficeKind? kind, string? office, string? state, int? district)
    {
        if (kind is null) return office;
        var role = kind switch
        {
            PublishedOfficeKind.Representative => UsStates.HouseOfficeByNonStateCode.GetValueOrDefault(state ?? string.Empty) ?? "Representative",
            PublishedOfficeKind.Senator => "Senator",
            PublishedOfficeKind.Executive => ExecutiveRole(office) ?? office ?? kind.ToString(),
            _ => office ?? kind.ToString()
        };
        if (state is null) return role;
        return district is > 0 ? $"{role} for {state}, district {district}" : $"{role} for {state}";
    }

    /// <summary>The office words of an executive office code of the executive roster, such as "President" for "prez". Any other code has no words.</summary>
    public static string? ExecutiveRole(string? office) => office switch
    {
        "prez" => "President",
        "viceprez" => "Vice President",
        _ => null
    };
}
