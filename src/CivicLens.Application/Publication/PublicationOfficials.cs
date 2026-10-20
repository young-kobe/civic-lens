using CivicLens.Application.Collection;
using CivicLens.Application.Roster;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Joins configured people to roster terms. A person matches by bioguide ID, else by Wikidata ID, else has no terms.</summary>
public sealed class PublicationOfficials(CollectionConfiguration? configuration, IMemberRosterStore members, IExecutiveRosterStore executives)
{
    private static readonly PublishedOfficeTerm[] NoTerms = [];

    public async Task<PublishedOfficials> ResolveAsync(IReadOnlySet<string> officialIds, CancellationToken cancellationToken)
    {
        var people = (configuration?.People ?? []).Where(person => officialIds.Contains(person.Id))
            .OrderBy(person => person.Name, StringComparer.Ordinal).ThenBy(person => person.Id, StringComparer.Ordinal).ToArray();
        var memberTerms = await ReadMemberTermsAsync(people.Any(person => person.BioguideId is not null), cancellationToken);
        var needsExecutives = people.Any(person => person.WikidataId is not null && MemberTermsOf(person, memberTerms).Length == 0);
        var executiveTerms = await ReadExecutiveTermsAsync(needsExecutives, cancellationToken);
        var published = new PublishedOfficials
        {
            Officials = [.. people.Select(person => ToProfile(person, memberTerms, executiveTerms))]
        };
        published.Validate();
        return published;
    }

    private async Task<Dictionary<string, PublishedOfficeTerm[]>> ReadMemberTermsAsync(bool needed, CancellationToken cancellationToken)
    {
        if (!needed || configuration!.RosterSourceOf(ApiParser.CongressLegislators) is not { } source) return [];
        var roster = await members.ReadLatestAsync(source.Id, cancellationToken);
        return roster.ToDictionary(member => member.BioguideId, member => member.Terms.Select(ToTerm).ToArray(), StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, PublishedOfficeTerm[]>> ReadExecutiveTermsAsync(bool needed, CancellationToken cancellationToken)
    {
        if (!needed || configuration!.RosterSourceOf(ApiParser.CongressLegislatorsExecutive) is not { } source) return [];
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
        if (terms.Length > 0 || person.WikidataId is null) return terms;
        return executiveTerms.GetValueOrDefault(person.WikidataId, NoTerms);
    }

    private static PublishedOfficeTerm[] MemberTermsOf(PersonConfiguration person, Dictionary<string, PublishedOfficeTerm[]> memberTerms) =>
        person.BioguideId is null ? NoTerms : memberTerms.GetValueOrDefault(person.BioguideId, NoTerms);
}
