using CivicLens.Application.Collection;
using CivicLens.Application.People;

namespace CivicLens.Application.Roster;

public sealed class ProposeRosterChanges(IMemberRosterStore roster)
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
        var takenIds = configuration.People.Select(person => person.Id).ToHashSet(StringComparer.Ordinal);
        var joins = members.Where(member => !configured.ContainsKey(member.BioguideId)).OrderBy(member => member.BioguideId, StringComparer.Ordinal)
            .Select(member => new ProposedMember(PersonIdRule.Mint(member.FullName, member.BioguideId, takenIds), member.FullName, member.BioguideId)).ToArray();
        var leaves = configured.Values.Where(person => person.Offices is null && !current.ContainsKey(person.BioguideId!))
            .OrderBy(person => person.Id, StringComparer.Ordinal).ToArray();
        return new RosterProposal(joins, leaves);
    }
}
