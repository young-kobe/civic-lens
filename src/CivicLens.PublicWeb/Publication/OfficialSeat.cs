using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The one owner of the words that name the seat of an official in a list row.</summary>
public static class OfficialSeat
{
    public static string? Text(PublishedOfficeKind? kind, string? office, string? state, int? district)
    {
        if (kind is null) return office;
        var role = kind switch
        {
            PublishedOfficeKind.Representative => UsStates.HouseOfficeByNonStateCode.GetValueOrDefault(state ?? string.Empty) ?? "Representative",
            PublishedOfficeKind.Senator => "Senator",
            _ => office ?? kind.ToString()
        };
        if (state is null) return role;
        return district is > 0 ? $"{role} for {state}, district {district}" : $"{role} for {state}";
    }
}
