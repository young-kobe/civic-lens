using CivicLens.Application.Collection;
using CivicLens.Application.Roster;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Builds the profile of each official that a record names. A configured person matches roster terms by bioguide ID, else by
/// Wikidata ID, else gets the configured offices. The website is the valid roster URL of a member, else the configured website. A person who left the configuration keeps the name that the records hold, and has no terms.
/// </summary>
public sealed class PublicationOfficials(CollectionConfiguration configuration, IMemberRosterStore members, IExecutiveRosterStore executives)
{
    private static readonly PublishedOfficeTerm[] NoTerms = [];

    private readonly HashSet<string> configuredIds = [.. configuration.People.Select(person => person.Id)];

    public bool IsConfigured(string officialId) => configuredIds.Contains(officialId);

    public async Task<PublishedOfficials> ResolveAsync(IReadOnlySet<string> officialIds,
        IReadOnlyDictionary<string, string> recordNames, CancellationToken cancellationToken)
    {
        var people = configuration.People.Where(person => officialIds.Contains(person.Id)).ToArray();
        var memberFacts = await ReadMemberFactsAsync(people.Any(person => person.BioguideId is not null), cancellationToken);
        var needsExecutives = people.Any(person => person.WikidataId is not null && MemberTermsOf(person, memberFacts).Length == 0);
        var executiveTerms = await ReadExecutiveTermsAsync(needsExecutives, cancellationToken);
        var published = new PublishedOfficials
        {
            Officials = [.. people.Select(person => ToProfile(person, memberFacts, executiveTerms))
                .Concat(officialIds.Except(people.Select(person => person.Id)).Select(id => Unconfigured(id, recordNames)))
                .OrderBy(profile => profile.Name, StringComparer.Ordinal).ThenBy(profile => profile.Id, StringComparer.Ordinal)]
        };
        published.Validate();
        return published;
    }

    private async Task<Dictionary<string, MemberFacts>> ReadMemberFactsAsync(bool needed, CancellationToken cancellationToken)
    {
        if (!needed || configuration.RosterSourceOf(ApiParser.CongressLegislators) is not { } source) return [];
        var roster = await members.ReadLatestAsync(source.Id, cancellationToken);
        return roster.ToDictionary(member => member.BioguideId, member => new MemberFacts([.. member.Terms.Select(ToTerm)], ValidWebsite(member.Url)), StringComparer.Ordinal);
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

    private static PublishedOfficialProfile Unconfigured(string id, IReadOnlyDictionary<string, string> recordNames) =>
        recordNames.TryGetValue(id, out var name)
            ? new() { Id = id, Name = name, Terms = NoTerms }
            : throw new InvalidDataException($"Official {id} is named by a record but no record holds a name for the official.");

    private static PublishedOfficialProfile ToProfile(PersonConfiguration person, Dictionary<string, MemberFacts> memberFacts,
        Dictionary<string, PublishedOfficeTerm[]> executiveTerms) => new()
        {
            Id = person.Id,
            Name = person.Name,
            WikidataId = person.WikidataId,
            Website = WebsiteOf(person, memberFacts),
            Terms = TermsOf(person, memberFacts, executiveTerms)
        };

    private static PublishedOfficeTerm[] TermsOf(PersonConfiguration person, Dictionary<string, MemberFacts> memberFacts,
        Dictionary<string, PublishedOfficeTerm[]> executiveTerms)
    {
        var terms = MemberTermsOf(person, memberFacts);
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

    private static PublishedOfficeTerm[] MemberTermsOf(PersonConfiguration person, Dictionary<string, MemberFacts> memberFacts) =>
        person.BioguideId is not null && memberFacts.TryGetValue(person.BioguideId, out var facts) ? facts.Terms : NoTerms;

    private static string? WebsiteOf(PersonConfiguration person, Dictionary<string, MemberFacts> memberFacts) =>
        person.BioguideId is not null && memberFacts.TryGetValue(person.BioguideId, out var facts) && facts.Website is { } website ? website : person.Website;

    private static string? ValidWebsite(string? url) => PublishedWebsite.IsValid(url) ? url : null;

    private sealed record MemberFacts(PublishedOfficeTerm[] Terms, string? Website);
}
