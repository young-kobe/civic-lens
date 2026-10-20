using CivicLens.Application.Collection;
using CivicLens.Application.Roster;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Builds the profile of each official that a record names. A configured person matches roster terms by bioguide ID, else by
/// Wikidata ID, else gets the configured offices. A person who left the configuration keeps the profile of the previous release.
/// </summary>
public sealed class PublicationOfficials(CollectionConfiguration configuration, IMemberRosterStore members, IExecutiveRosterStore executives)
{
    private static readonly PublishedOfficeTerm[] NoTerms = [];

    public async Task<PublishedOfficials> ResolveAsync(IReadOnlySet<string> officialIds,
        IReadOnlyDictionary<string, PublishedOfficialProfile> previous, CancellationToken cancellationToken)
    {
        var people = configuration.People.Where(person => officialIds.Contains(person.Id)).ToArray();
        var memberTerms = await ReadMemberTermsAsync(people.Any(person => person.BioguideId is not null), cancellationToken);
        var needsExecutives = people.Any(person => person.WikidataId is not null && MemberTermsOf(person, memberTerms).Length == 0);
        var executiveTerms = await ReadExecutiveTermsAsync(needsExecutives, cancellationToken);
        var published = new PublishedOfficials
        {
            Officials = [.. people.Select(person => ToProfile(person, memberTerms, executiveTerms))
                .Concat(officialIds.Except(people.Select(person => person.Id)).Select(id => Carried(id, previous)))
                .OrderBy(profile => profile.Name, StringComparer.Ordinal).ThenBy(profile => profile.Id, StringComparer.Ordinal)]
        };
        published.Validate();
        return published;
    }

    private async Task<Dictionary<string, PublishedOfficeTerm[]>> ReadMemberTermsAsync(bool needed, CancellationToken cancellationToken)
    {
        if (!needed || configuration.RosterSourceOf(ApiParser.CongressLegislators) is not { } source) return [];
        var roster = await members.ReadLatestAsync(source.Id, cancellationToken);
        return roster.ToDictionary(member => member.BioguideId, member => member.Terms.Select(ToTerm).ToArray(), StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, PublishedOfficeTerm[]>> ReadExecutiveTermsAsync(bool needed, CancellationToken cancellationToken)
    {
        if (!needed || configuration.RosterSourceOf(ApiParser.CongressLegislatorsExecutive) is not { } source) return [];
        var roster = await executives.ReadLatestAsync(source.Id, cancellationToken);
        return roster.Where(official => official.WikidataId is not null)
            .ToDictionary(official => official.WikidataId!, official => official.Terms.Select(ToTerm).ToArray(), StringComparer.Ordinal);
    }

    private static PublishedOfficeTerm ToTerm(RosterTerm term) => new()
    {
        Office = term.Chamber,
        State = term.State,
        District = term.District,
        Party = term.Party,
        Start = term.Start,
        End = term.End
    };

    private static PublishedOfficeTerm ToTerm(ExecutiveTerm term) => new()
    {
        Office = term.Office,
        Party = term.Party,
        Start = term.Start,
        End = term.End
    };

    private static PublishedOfficialProfile Carried(string id, IReadOnlyDictionary<string, PublishedOfficialProfile> previous) =>
        previous.TryGetValue(id, out var profile)
            ? profile
            : throw new InvalidDataException($"Official {id} is named by a record but has no configured name or earlier profile.");

    private static PublishedOfficialProfile ToProfile(PersonConfiguration person, Dictionary<string, PublishedOfficeTerm[]> memberTerms,
        Dictionary<string, PublishedOfficeTerm[]> executiveTerms) => new()
        {
            Id = person.Id,
            Name = person.Name,
            WikidataId = person.WikidataId,
            Terms = TermsOf(person, memberTerms, executiveTerms)
        };

    private static PublishedOfficeTerm[] TermsOf(PersonConfiguration person, Dictionary<string, PublishedOfficeTerm[]> memberTerms,
        Dictionary<string, PublishedOfficeTerm[]> executiveTerms)
    {
        var terms = MemberTermsOf(person, memberTerms);
        if (terms.Length == 0 && person.WikidataId is not null) terms = executiveTerms.GetValueOrDefault(person.WikidataId, NoTerms);
        return terms.Length > 0 ? terms : [.. (person.Offices ?? []).Select(ToTerm)];
    }

    private static PublishedOfficeTerm ToTerm(OfficeConfiguration office) => new()
    {
        Office = office.Title,
        Party = office.Party,
        Start = office.StartsOn,
        End = office.EndsOn
    };

    private static PublishedOfficeTerm[] MemberTermsOf(PersonConfiguration person, Dictionary<string, PublishedOfficeTerm[]> memberTerms) =>
        person.BioguideId is null ? NoTerms : memberTerms.GetValueOrDefault(person.BioguideId, NoTerms);
}
