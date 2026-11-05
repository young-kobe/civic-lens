using CivicLens.Application.Collection;

namespace CivicLens.Application.Roster;

/// <summary>Reads the latest member roster of the configured source. Publication and the owner website command read it through this one owner.</summary>
public sealed class MemberRoster(CollectionConfiguration configuration, IMemberRosterStore members)
{
    public async Task<IReadOnlyList<RosterMember>> ReadAsync(CancellationToken cancellationToken) =>
        configuration.SourceOf(ApiParser.CongressLegislators) is { } source
            ? await members.ReadLatestAsync(source.Id, cancellationToken)
            : [];

    /// <summary>Returns the roster website that publishes for a configured person, or null when the person is not a member or the roster URL is not valid.</summary>
    public async Task<string?> PublishedWebsiteOfAsync(string personId, CancellationToken cancellationToken)
    {
        var bioguideId = configuration.People.FirstOrDefault(person => person.Id == personId)?.BioguideId;
        if (bioguideId is null) return null;
        var roster = await ReadAsync(cancellationToken);
        return roster.FirstOrDefault(member => member.BioguideId == bioguideId)?.PublishableWebsite;
    }
}
