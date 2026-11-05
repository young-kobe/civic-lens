using CivicLens.Application.Collection;

namespace CivicLens.Application.Roster;

/// <summary>Reads the latest member roster of the configured source. Publication and the owner website command read it through this one owner.</summary>
public sealed class MemberRoster(CollectionConfiguration configuration, IMemberRosterStore members)
{
    public async Task<IReadOnlyList<RosterMember>> ReadAsync(CancellationToken cancellationToken) =>
        configuration.SourceOf(ApiParser.CongressLegislators) is { } source
            ? await members.ReadLatestAsync(source.Id, cancellationToken)
            : [];

    /// <summary>Returns the website that publishes for a configured person. A person who is not in the latest roster keeps the saved website.</summary>
    public async Task<string?> PublishedWebsiteOfAsync(string personId, string? savedWebsite, CancellationToken cancellationToken)
    {
        var bioguideId = configuration.People.FirstOrDefault(person => person.Id == personId)?.BioguideId;
        if (bioguideId is null) return savedWebsite;
        var roster = await ReadAsync(cancellationToken);
        return roster.FirstOrDefault(member => member.BioguideId == bioguideId)?.WebsiteOver(savedWebsite) ?? savedWebsite;
    }
}
