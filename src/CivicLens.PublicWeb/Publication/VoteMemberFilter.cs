namespace CivicLens.PublicWeb.Publication;

/// <summary>The filters of the member table. Each filter that is set narrows the rows. A name matches any part of a member name, ignoring case. The party value "none" matches the members with no known party.</summary>
public sealed record VoteMemberFilter(string? Name = null, string? Cast = null, string? Party = null)
{
    /// <summary>The party filter value for the members who have no known party.</summary>
    public const string NoKnownParty = "none";

    public bool IsActive => Name is not null || Cast is not null || Party is not null;

    public bool Matches(VoteMemberData member) =>
        (Name is null || member.Name.Contains(Name, StringComparison.OrdinalIgnoreCase)) &&
        (Cast is null || member.Cast == Cast) &&
        (Party is null || (Party == NoKnownParty ? member.Party is null : member.Party == Party));
}
