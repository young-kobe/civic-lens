namespace CivicLens.PublicWeb.Publication;

/// <summary>The filters of the member table. Each filter that is set narrows the rows. A name matches any part of a member name, ignoring case.</summary>
public sealed record VoteMemberFilter(string? Name = null, string? Cast = null, string? Party = null)
{
    public bool IsActive => Name is not null || Cast is not null || Party is not null;

    public bool Matches(VoteMemberData member) =>
        (Name is null || member.Name.Contains(Name, StringComparison.OrdinalIgnoreCase)) &&
        (Cast is null || member.Cast == Cast) &&
        (Party is null || member.Party == Party);
}
