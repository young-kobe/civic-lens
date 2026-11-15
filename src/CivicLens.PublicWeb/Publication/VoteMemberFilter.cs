namespace CivicLens.PublicWeb.Publication;

/// <summary>The filters of the member table. Each filter that is set narrows the rows. A name matches any part of a member name, ignoring case. The party value "none" matches the members with no known party.</summary>
public sealed record VoteMemberFilter(string? Name = null, string? Cast = null, string? Party = null)
{
    /// <summary>The party filter value for the members who have no known party.</summary>
    public const string NoKnownParty = "none";

    /// <summary>The line for a filter that matches no tracked member. The page script shows it too when the typed filter matches no row.</summary>
    public const string NoMatch = "No tracked member matches these filters.";

    public bool IsActive => Name is not null || Cast is not null || Party is not null;

    public bool Matches(VoteMemberData member) =>
        (Name is null || member.Name.Contains(Name, StringComparison.OrdinalIgnoreCase)) &&
        (Cast is null || member.Cast == Cast) &&
        (Party is null || (Party == NoKnownParty ? member.Party is null : member.Party == Party));

    /// <summary>The keys of one member row that the page script matches against the cast and party fields while the reader types: <c>cast:</c> and <c>party:</c>,
    /// each with the escaped field value, so a value with a space stays one key. The party key of a member with no known party is <see cref="NoKnownParty"/>.</summary>
    public static string Keys(VoteMemberData member) =>
        $"cast:{Uri.EscapeDataString(member.Cast)} party:{Uri.EscapeDataString(member.Party ?? NoKnownParty)}";
}
