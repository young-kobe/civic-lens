using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Roster;

public sealed record RosterMember(string BioguideId, string FullName, string? Office, string? Phone,
    string? Url, RosterTerm[] Terms)
{
    /// <summary>The one owner of the website precedence. A roster URL that follows the website rule wins over the saved website. Else the saved website publishes.</summary>
    public string? WebsiteOver(string? savedWebsite) => PublishedWebsite.IsValid(Url) ? Url : savedWebsite;
}
