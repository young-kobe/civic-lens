using CivicLens.Application.Collection;
using CivicLens.Application.People;
using CivicLens.Application.Roster;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Builds the profile of each official that a record names. A configured person matches roster terms by bioguide ID, else by
/// Wikidata ID, else gets the configured offices. The roster entry that gives the terms also gives the sort name. A person whose newest setting withholds the photo gets no photo. The website is the valid roster URL of a member, else the website of the newest setting. A person who left the configuration keeps the name that the records hold, and has no terms.
/// </summary>
public sealed class PublicationOfficials(CollectionConfiguration configuration, IMemberRosterStore members, IExecutiveRosterStore executives,
    IPersonRegistryStore people)
{
    private static readonly PublishedOfficeTerm[] NoTerms = [];

    private readonly MemberRoster memberRoster = new(configuration, members);
    private readonly HashSet<string> configuredIds = [.. configuration.People.Select(person => person.Id)];

    public bool IsConfigured(string officialId) => configuredIds.Contains(officialId);

    internal Task<IReadOnlyList<RosterMember>> ReadRosterAsync(CancellationToken cancellationToken) => memberRoster.ReadAsync(cancellationToken);

    /// <summary>Reads the newest setting of every configured person once, so the photo choice and the website of one publish agree.</summary>
    internal async Task<IReadOnlyDictionary<string, PersonSetting>> ReadSettingsAsync(CancellationToken cancellationToken) =>
        await people.ReadNewestSettingsAsync([.. configuration.People.Select(person => person.Id)], cancellationToken);

    internal async Task<IReadOnlyList<OfficialPhoto>> ReadPhotosAsync(IReadOnlyDictionary<string, PersonSetting> settings,
        CancellationToken cancellationToken) =>
        await members.ReadNewestPhotosAsync([.. configuration.People.Where(person => !SettingOf(settings, person.Id).WithholdPhoto).Select(person => person.Id)],
            cancellationToken);

    internal async Task<ResolvedOfficials> ResolveAsync(IReadOnlySet<string> officialIds, IReadOnlyDictionary<string, string> recordNames,
        IReadOnlyList<RosterMember> rosterMembers, IReadOnlyDictionary<string, PersonSetting> settings,
        IReadOnlyDictionary<string, PublishedPhoto> photos, CancellationToken cancellationToken)
    {
        var people = configuration.People.Where(person => officialIds.Contains(person.Id)).ToArray();
        var (memberFacts, executives) = await ReadRostersAsync(people, rosterMembers, cancellationToken);
        var published = new PublishedOfficials
        {
            Officials = [.. people.Select(person => ToProfile(person, memberFacts, executives, settings, photos))
                .Concat(officialIds.Except(people.Select(person => person.Id)).Select(id => Unconfigured(id, recordNames)))
                .OrderBy(profile => profile.Name, StringComparer.Ordinal).ThenBy(profile => profile.Id, StringComparer.Ordinal)]
        };
        published.Validate();
        return new(published, SortNamesOf(people, memberFacts, executives));
    }

    /// <summary>Reads the sort name of every configured person from the latest rosters, for rows that a release wrote before it stored sort names.</summary>
    internal async Task<IReadOnlyDictionary<string, string>> ReadSortNamesAsync(CancellationToken cancellationToken)
    {
        var people = configuration.People.ToArray();
        var rosterMembers = people.Any(person => person.BioguideId is not null) ? await ReadRosterAsync(cancellationToken) : [];
        var (memberFacts, executives) = await ReadRostersAsync(people, rosterMembers, cancellationToken);
        return SortNamesOf(people, memberFacts, executives);
    }

    private async Task<(Dictionary<string, MemberFacts> MemberFacts, Dictionary<string, ExecutiveOfficial> Executives)> ReadRostersAsync(
        PersonConfiguration[] people, IReadOnlyList<RosterMember> rosterMembers, CancellationToken cancellationToken)
    {
        var memberFacts = people.Any(person => person.BioguideId is not null) ? MemberFactsOf(rosterMembers) : [];
        var needsExecutives = people.Any(person => person.WikidataId is not null && MemberTermsOf(person, memberFacts).Length == 0);
        return (memberFacts, await ReadExecutivesAsync(needsExecutives, cancellationToken));
    }

    /// <summary>The one owner of the sort name: the roster last name of the roster entry that gives the terms. A person with no roster entry has none, and the view sorts the person by name.</summary>
    private static Dictionary<string, string> SortNamesOf(PersonConfiguration[] people, Dictionary<string, MemberFacts> memberFacts,
        Dictionary<string, ExecutiveOfficial> executives) =>
        people.Select(person => (person.Id, SortName: SortNameOf(person, memberFacts, executives)))
            .Where(pair => pair.SortName is not null)
            .ToDictionary(pair => pair.Id, pair => pair.SortName!, StringComparer.Ordinal);

    private static string? SortNameOf(PersonConfiguration person, Dictionary<string, MemberFacts> memberFacts, Dictionary<string, ExecutiveOfficial> executives)
    {
        if (MemberTermsOf(person, memberFacts).Length > 0) return memberFacts[person.BioguideId!].Member.LastName;
        return person.WikidataId is not null && executives.TryGetValue(person.WikidataId, out var executive) && executive.Terms.Length > 0 ? executive.LastName : null;
    }

    private static Dictionary<string, MemberFacts> MemberFactsOf(IReadOnlyList<RosterMember> rosterMembers) =>
        rosterMembers.ToDictionary(member => member.BioguideId, member => new MemberFacts([.. member.Terms.Select(ToTerm)], member), StringComparer.Ordinal);

    private async Task<Dictionary<string, ExecutiveOfficial>> ReadExecutivesAsync(bool needed, CancellationToken cancellationToken)
    {
        if (!needed || configuration.SourceOf(ApiParser.CongressLegislatorsExecutive) is not { } source) return [];
        var roster = await executives.ReadLatestAsync(source.Id, cancellationToken);
        return roster.Where(official => official.WikidataId is not null).ToDictionary(official => official.WikidataId!, StringComparer.Ordinal);
    }

    private static PublishedOfficeTerm ToTerm(RosterTerm term) => new()
    {
        Office = term.Chamber,
        Kind = term.Chamber switch
        {
            MemberRosterParser.Senate => PublishedOfficeKind.Senator,
            MemberRosterParser.House => PublishedOfficeKind.Representative,
            _ => throw new InvalidDataException($"Roster term has unknown chamber '{term.Chamber}'.")
        },
        State = term.State,
        District = term.District,
        Party = term.Party,
        Caucus = term.Caucus,
        Start = term.Start,
        End = term.End,
        SenateClass = term.SenateClass
    };

    private static PublishedOfficeTerm ToTerm(ExecutiveTerm term) => new()
    {
        Office = term.Office,
        Kind = PublishedOfficeKind.Executive,
        Party = term.Party,
        Start = term.Start,
        End = term.End
    };

    private static PublishedOfficialProfile Unconfigured(string id, IReadOnlyDictionary<string, string> recordNames) =>
        recordNames.TryGetValue(id, out var name)
            ? new() { Id = id, Name = name, Terms = NoTerms }
            : throw new InvalidDataException($"Official {id} is named by a record but no record holds a name for the official.");

    private static PublishedOfficialProfile ToProfile(PersonConfiguration person, Dictionary<string, MemberFacts> memberFacts,
        Dictionary<string, ExecutiveOfficial> executives, IReadOnlyDictionary<string, PersonSetting> settings,
        IReadOnlyDictionary<string, PublishedPhoto> photos) => new()
        {
            Id = person.Id,
            Name = person.Name,
            WikidataId = person.WikidataId,
            Website = WebsiteOf(person, memberFacts, SettingOf(settings, person.Id)),
            Photo = photos.GetValueOrDefault(person.Id),
            Terms = TermsOf(person, memberFacts, executives)
        };

    private static PublishedOfficeTerm[] TermsOf(PersonConfiguration person, Dictionary<string, MemberFacts> memberFacts,
        Dictionary<string, ExecutiveOfficial> executives)
    {
        var terms = MemberTermsOf(person, memberFacts);
        if (terms.Length == 0 && person.WikidataId is not null && executives.TryGetValue(person.WikidataId, out var executive))
            terms = [.. executive.Terms.Select(ToTerm)];
        return terms.Length > 0 ? terms : [.. (person.Offices ?? []).Select(ToTerm)];
    }

    private static PublishedOfficeTerm ToTerm(OfficeConfiguration office) => new()
    {
        Office = office.Title,
        Kind = office.Kind switch
        {
            null => PublishedOfficeKind.Other,
            ConfiguredOfficeKind.Executive => PublishedOfficeKind.Executive,
            ConfiguredOfficeKind.Judicial => PublishedOfficeKind.Judicial,
            _ => throw new ArgumentOutOfRangeException(nameof(office), office.Kind, "Office kind has no published form.")
        },
        Party = office.Party,
        Start = office.StartsOn,
        End = office.EndsOn
    };

    private static PublishedOfficeTerm[] MemberTermsOf(PersonConfiguration person, Dictionary<string, MemberFacts> memberFacts) =>
        person.BioguideId is not null && memberFacts.TryGetValue(person.BioguideId, out var facts) ? facts.Terms : NoTerms;

    private static string? WebsiteOf(PersonConfiguration person, Dictionary<string, MemberFacts> memberFacts, PersonSetting setting) =>
        person.BioguideId is not null && memberFacts.TryGetValue(person.BioguideId, out var facts) ? facts.Member.WebsiteOver(setting.Website) : setting.Website;

    private static PersonSetting SettingOf(IReadOnlyDictionary<string, PersonSetting> settings, string personId) =>
        settings.GetValueOrDefault(personId, PersonSetting.None);

    private sealed record MemberFacts(PublishedOfficeTerm[] Terms, RosterMember Member);
}
