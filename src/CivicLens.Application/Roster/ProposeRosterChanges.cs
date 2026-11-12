using CivicLens.Application.Collection;
using CivicLens.Application.People;

namespace CivicLens.Application.Roster;

public sealed class ProposeRosterChanges(IMemberRosterStore roster, IPersonRegistryStore people)
{
    public async Task<RosterProposal?> ExecuteAsync(CollectionConfiguration configuration, CancellationToken cancellationToken)
    {
        var source = configuration.SourceOf(ApiParser.CongressLegislators)
            ?? throw new ArgumentException("The configuration has no member roster source.");
        var members = await roster.ReadLatestAsync(source.Id, cancellationToken);
        if (members.Count == 0) return null;

        var configured = configuration.People.Where(person => person.BioguideId is not null)
            .ToDictionary(person => person.BioguideId!, StringComparer.Ordinal);
        var current = members.ToDictionary(member => member.BioguideId, StringComparer.Ordinal);
        var newMembers = members.Where(member => !configured.ContainsKey(member.BioguideId))
            .OrderBy(member => member.BioguideId, StringComparer.Ordinal).ToArray();
        // The people table owns minted IDs. A row can outlive its member, so the configuration cannot say which IDs are taken.
        var minted = await people.ReadIdsByBioguideAsync(newMembers.Select(member => member.BioguideId).ToArray(), cancellationToken);
        var takenIds = configuration.People.Select(person => person.Id).Concat(minted.Values).ToHashSet(StringComparer.Ordinal);
        var joins = newMembers.Select(member => new ProposedMember(
            minted.TryGetValue(member.BioguideId, out var id) ? id : PersonIdRule.Mint(member.FullName, member.BioguideId, takenIds),
            member.FullName, member.BioguideId)).ToArray();
        var leaves = configured.Values.Where(person => person.Offices is null && !current.ContainsKey(person.BioguideId!))
            .OrderBy(person => person.Id, StringComparer.Ordinal).ToArray();
        return new RosterProposal(joins, leaves);
    }
}
