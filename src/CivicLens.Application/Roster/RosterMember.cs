using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Roster;

public sealed record RosterMember(string BioguideId, string FullName, string? Office, string? Phone,
    string? Url, RosterTerm[] Terms)
{
    /// <summary>The roster URL when it follows the website rule, else null. A valid roster URL wins over the website setting of the person.</summary>
    public string? PublishableWebsite => PublishedWebsite.IsValid(Url) ? Url : null;
}
