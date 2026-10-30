using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Documents;
using CivicLens.Application.Issues;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Registry;
using CivicLens.Publication.Contracts;
using System.Text.Json.Serialization;

namespace CivicLens.Application.Collection;

public sealed record CollectionConfiguration
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    public required PersonConfiguration[] People { get; init; }
    public required WatchedSourceConfiguration[] Sources { get; init; }
    public PacingGroupConfiguration[]? PacingGroups { get; init; }
    public DocumentProfileConfiguration[]? DocumentProfiles { get; init; }

    public PacingGroupConfiguration? PacingGroupOf(string allowedOrigin)
    {
        if (PacingGroups is null) return null;
        var host = new Uri(allowedOrigin).IdnHost.ToLowerInvariant();
        return PacingGroups.Where(group => group.Matches(host)).MaxBy(group => group.HostSuffix.Length);
    }

    public WatchedSourceConfiguration? RosterSourceOf(ApiParser parser) =>
        Sources.FirstOrDefault(source => source.ApiParser == parser);

    public void Validate()
    {
        if (Version != 1)
            throw new ArgumentException("Unsupported collection configuration version.");
        if (People is null || Sources is null)
            throw new ArgumentException("People and sources are required.");

        var peopleById = new Dictionary<string, PersonConfiguration>(StringComparer.Ordinal);
        var bioguideIds = new HashSet<string>(StringComparer.Ordinal);
        var wikidataIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var person in People)
        {
            if (person is null || string.IsNullOrWhiteSpace(person.Id) || string.IsNullOrWhiteSpace(person.Name))
                throw new ArgumentException("Every person must have a nonblank ID and name.");
            IssueConfiguration.ValidateId(person.Id, "Person");
            ValidateNames(person);
            ValidateOffices(person);
            ValidateExternalId(person, person.BioguideId, "bioguide", BioguideId.IsValid, bioguideIds);
            ValidateExternalId(person, person.WikidataId, "Wikidata", WikidataId.IsValid, wikidataIds);
            if (person.Website is not null && !PublishedWebsite.IsValid(person.Website))
                throw new ArgumentException($"Person '{person.Id}' website must be an absolute https URL of at most {PublishedWebsite.MaximumLength} characters with no user info.");
            if (!peopleById.TryAdd(person.Id, person))
                throw new ArgumentException($"Duplicate person ID '{person.Id}'.");
        }

        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        var profilesById = new Dictionary<string, DocumentProfileConfiguration>(StringComparer.Ordinal);
        if (DocumentProfiles is not null)
        {
            foreach (var profile in DocumentProfiles)
            {
                if (profile is null)
                    throw new ArgumentException("Document profiles cannot contain null entries.");
                _ = profile.ToProfile();
                if (!profilesById.TryAdd(profile.Id, profile))
                    throw new ArgumentException($"Duplicate document profile ID '{profile.Id}'.");
            }
        }

        ValidatePacingGroups();

        if (Sources.Where(source => CoversNoPeople(source?.ApiParser)).GroupBy(source => source.ApiParser).Any(kind => kind.Count() > 1))
            throw new ArgumentException("A configuration may have only one roster or photo source of each kind.");

        if (Sources.Count(source => source is { Enabled: true } && IsSenateVoteSource(source.ApiParser)) > 1)
            throw new ArgumentException("A configuration may enable only one Senate vote source.");

        var sourceUrls = new HashSet<Uri>();
        foreach (var source in Sources)
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Id))
                throw new ArgumentException("Every source must have an ID.");
            ValidateApiParser(source);
            ValidateCoverageShape(source);
            ValidateStatementTextInFeed(source);
            if (!sourceIds.Add(source.Id))
                throw new ArgumentException($"Duplicate source ID '{source.Id}'.");
            if (source.DocumentProfileId is not null && !profilesById.ContainsKey(source.DocumentProfileId))
                throw new ArgumentException($"Source '{source.Id}' references a missing document profile.");

            if (!Uri.TryCreate(source.Url, UriKind.Absolute, out var url))
                throw new ArgumentException($"Source '{source.Id}' must have an absolute URL.");
            if (!sourceUrls.Add(url))
                throw new ArgumentException("A source URL may be configured only once; add people to its membership list.");

            if (source.ApiParser is null)
                ValidateDatedCoverage(source, peopleById);

            var request = CollectionJobDefinition.FromSource(source).CreateRequest("validation-job", "collection-artifacts");
            request.Validate();
            source.JobPolicy?.Validate(request);
            source.AdmissionPolicy?.Validate();
            if (source.ApiParser is ApiParser.CongressBillList or ApiParser.CongressHouseVoteList)
                ValidateListAdmissionCapacity(source, ApiPageQuery.PageLimit + 1);
            if (source.ApiParser == ApiParser.SenateVoteList)
                ValidateListAdmissionCapacity(source, SenateVoteMenuParser.MaximumVotes + 1);
            if (source.ApiParser == ApiParser.VoteviewSenateVoteList)
                ValidateListAdmissionCapacity(source, VoteviewFollowOnJobs);
            if (source.ApiParser == ApiParser.MemberPhotos)
                ValidateListAdmissionCapacity(source, People.Count(person => person.BioguideId is not null));
            if (source.CheckIntervalSeconds is { } interval && interval is
                < WatchedSourceConfiguration.MinimumCheckIntervalSeconds or
                > WatchedSourceConfiguration.MaximumCheckIntervalSeconds)
                throw new ArgumentException($"Source '{source.Id}' check interval must be from " +
                    $"{WatchedSourceConfiguration.MinimumCheckIntervalSeconds} to " +
                    $"{WatchedSourceConfiguration.MaximumCheckIntervalSeconds} seconds.");
        }
    }

    public WatchedSourceConfiguration EligibleSource(string sourceId, DateOnly asOf)
    {
        var source = Sources.FirstOrDefault(candidate => candidate is not null && candidate.Id == sourceId)
            ?? throw new ArgumentException($"Unknown source ID '{sourceId}'.", nameof(sourceId));
        if (!source.Enabled)
            throw new ArgumentException($"Source '{sourceId}' is disabled.", nameof(sourceId));
        if (!IsActiveOn(source, asOf))
            throw new ArgumentException($"Source '{sourceId}' has no active person coverage on {asOf:yyyy-MM-dd}.", nameof(sourceId));
        return source;
    }

    public bool IsActiveOn(WatchedSourceConfiguration source, DateOnly asOf) =>
        CoversNoPeople(source.ApiParser) || CoveredPersonIdsOf(source, asOf).Length > 0;

    public string[] CoveredPersonIdsOf(WatchedSourceConfiguration source, DateOnly asOf)
    {
        IEnumerable<string> ids = source.ApiParser switch
        {
            ApiParser.CongressBillList or ApiParser.CongressHouseVoteList or ApiParser.SenateVoteList or ApiParser.VoteviewSenateVoteList => asOf < source.BackfillFrom ? [] : BioguidePersonIds(),
            ApiParser.CongressLegislators or ApiParser.CongressLegislatorsExecutive or ApiParser.MemberPhotos => [],
            null => source.Coverage!.Where(coverage => new DateRange(coverage.StartsOn, coverage.EndsBefore).Contains(asOf))
                .Select(coverage => coverage.PersonId),
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        return ids.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlySet<string> CoveredBioguideIdsOf(WatchedSourceConfiguration source, DateOnly asOf) =>
        CoveredBioguidePeopleOf(source, asOf).Select(person => person.BioguideId!).ToHashSet(StringComparer.Ordinal);

    public PersonConfiguration[] CoveredBioguidePeopleOf(WatchedSourceConfiguration source, DateOnly asOf)
    {
        var covered = CoveredPersonIdsOf(source, asOf).ToHashSet(StringComparer.Ordinal);
        return [.. People.Where(person => person.BioguideId is not null && covered.Contains(person.Id))];
    }

    public string[] ListedPersonIdsOf(WatchedSourceConfiguration source) =>
        source.ApiParser is null
            ? [.. source.Coverage!.Select(coverage => coverage.PersonId).Distinct(StringComparer.Ordinal)]
            : [];

    private IEnumerable<string> BioguidePersonIds() =>
        People.Where(person => person.BioguideId is not null).Select(person => person.Id);

    private static void ValidateApiParser(WatchedSourceConfiguration source)
    {
        if (source.ApiParser is null)
        {
            if (source.Mode == CollectionMode.Api)
                throw new ArgumentException($"Source '{source.Id}' in API mode needs an API parser.");
            if (source.MinimumCongress is not null || source.BackfillFrom is not null)
                throw new ArgumentException($"Source '{source.Id}' sets API parser options without an API parser.");
            return;
        }
        if (source.Mode != CollectionMode.Api)
            throw new ArgumentException($"Source '{source.Id}' can set an API parser only in API mode.");
        switch (source.ApiParser)
        {
            case ApiParser.CongressBillList:
            case ApiParser.CongressHouseVoteList:
                ValidateCongressApiSource(source);
                break;
            case ApiParser.SenateVoteList:
            case ApiParser.VoteviewSenateVoteList:
                ValidateSenateVoteSource(source);
                break;
            case ApiParser.CongressLegislators:
            case ApiParser.CongressLegislatorsExecutive:
            case ApiParser.MemberPhotos:
                ValidatePublicFileSource(source);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(source));
        }
    }

    private const int VoteviewFollowOnJobs = 2;

    private static bool IsSenateVoteSource(ApiParser? parser) =>
        parser is ApiParser.SenateVoteList or ApiParser.VoteviewSenateVoteList;

    private static bool CoversNoPeople(ApiParser? parser) => IsRosterFile(parser) || parser == ApiParser.MemberPhotos;

    private static bool IsRosterFile(ApiParser? parser) =>
        parser is ApiParser.CongressLegislators or ApiParser.CongressLegislatorsExecutive;

    private static void ValidatePublicFileSource(WatchedSourceConfiguration source)
    {
        if (source.Credential is not null)
            throw new ArgumentException($"Source '{source.Id}' reads a public file and cannot set a credential.");
        if (source.ETag is not null || source.LastModified is not null)
            throw new ArgumentException($"Source '{source.Id}' downloads its whole file on every check and cannot set validators.");
        if (source.Coverage is not null || source.MinimumCongress is not null || source.BackfillFrom is not null)
            throw new ArgumentException($"Source '{source.Id}' covers no people and sets no Congress options.");
    }

    private static void ValidateCongressApiSource(WatchedSourceConfiguration source)
    {
        if (source.Credential is null)
            throw new ArgumentException($"Source '{source.Id}' needs a credential for its API parser.");
        ValidateSessionWindow(source);
    }

    private static void ValidateSenateVoteSource(WatchedSourceConfiguration source)
    {
        if (source.Credential is not null)
            throw new ArgumentException($"Source '{source.Id}' reads public files and cannot set a credential.");
        ValidateSessionWindow(source);
    }

    private static void ValidateSessionWindow(WatchedSourceConfiguration source)
    {
        if (source.MinimumCongress is not >= 1 || source.BackfillFrom is null)
            throw new ArgumentException($"Source '{source.Id}' needs a minimum Congress of 1 or more and a backfill date.");
        if (source.Coverage is not null)
            throw new ArgumentException($"Source '{source.Id}' covers every person with a bioguide ID and cannot list people.");
    }

    private static void ValidateListAdmissionCapacity(WatchedSourceConfiguration source, int jobs)
    {
        var policy = source.AdmissionPolicy ?? new DiscoveryAdmissionPolicy();
        if (!policy.CanReserve(CollectionJobDefinition.FromSource(source), jobs))
            throw new ArgumentException($"Source '{source.Id}' admission policy must hold every job of one admission.");
    }

    private static void ValidateStatementTextInFeed(WatchedSourceConfiguration source)
    {
        if (source.StatementTextInFeed && source.Mode != CollectionMode.Feed)
            throw new ArgumentException($"Source '{source.Id}' can read statement text from a feed only in feed mode.");
    }

    private static void ValidateCoverageShape(WatchedSourceConfiguration source)
    {
        if (source.ApiParser is null && source.Coverage is null)
            throw new ArgumentException("Sources require Coverage.");
    }

    private static void ValidateDatedCoverage(WatchedSourceConfiguration source,
        IReadOnlyDictionary<string, PersonConfiguration> peopleById)
    {
        var coverage = source.Coverage!;
        if (coverage.Length == 0)
            throw new ArgumentException($"Source '{source.Id}' must define at least one coverage interval.");
        var intervalsByPerson = new Dictionary<string, List<DateRange>>(StringComparer.Ordinal);
        foreach (var item in coverage)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.PersonId) || !peopleById.ContainsKey(item.PersonId))
                throw new ArgumentException($"Source '{source.Id}' references a missing person.");
            var range = new DateRange(item.StartsOn, item.EndsBefore);
            if (!intervalsByPerson.TryGetValue(item.PersonId, out var ranges))
                intervalsByPerson.Add(item.PersonId, ranges = []);
            if (ranges.Any(existing => existing.Overlaps(range)))
                throw new ArgumentException($"Source '{source.Id}' has overlapping coverage intervals for person '{item.PersonId}'.");
            ranges.Add(range);
        }
    }

    private void ValidatePacingGroups()
    {
        if (PacingGroups is null) return;
        var suffixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in PacingGroups)
        {
            if (group is null)
                throw new ArgumentException("Pacing groups cannot contain null entries.");
            group.Validate();
            if (!suffixes.Add(group.HostSuffix))
                throw new ArgumentException($"Duplicate pacing group '{group.HostSuffix}'.");
        }
    }

    private static void ValidateNames(PersonConfiguration person)
    {
        if (person.Names is null)
            return;
        var names = new Dictionary<string, List<DateRange>>(StringComparer.Ordinal);
        foreach (var item in person.Names)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name))
                throw new ArgumentException($"Person '{person.Id}' has an invalid dated name.");
            var name = new DatedName(item.Name, new DateRange(item.StartsOn, item.EndsBefore));
            var range = name.Range;
            if (!names.TryGetValue(name.Name, out var ranges))
                names.Add(item.Name, ranges = []);
            if (ranges.Any(existing => existing.Overlaps(range)))
                throw new ArgumentException($"Person '{person.Id}' has overlapping intervals for name '{item.Name}'.");
            ranges.Add(range);
        }
    }

    private static void ValidateOffices(PersonConfiguration person)
    {
        if (person.Offices is null)
            return;
        if (person.Offices.Length > OfficeConfiguration.MaximumPerPerson)
            throw new ArgumentException($"Person '{person.Id}' has more than {OfficeConfiguration.MaximumPerPerson} offices.");
        foreach (var office in person.Offices)
        {
            if (office is null || string.IsNullOrWhiteSpace(office.Title) || office.Party is not null && string.IsNullOrWhiteSpace(office.Party) ||
                office.EndsOn < office.StartsOn)
                throw new ArgumentException($"Person '{person.Id}' has an invalid office.");
            IssueConfiguration.ValidateNameLength(office.Title, $"Person '{person.Id}' office title");
            if (office.Party is not null)
                IssueConfiguration.ValidateNameLength(office.Party, $"Person '{person.Id}' office party");
        }
    }

    private static void ValidateExternalId(PersonConfiguration person, string? id, string label, Func<string, bool> isValid,
        HashSet<string> seen)
    {
        if (id is null)
            return;
        if (!isValid(id))
            throw new ArgumentException($"Person '{person.Id}' has an invalid {label} ID.");
        if (!seen.Add(id))
            throw new ArgumentException($"Duplicate {label} ID '{id}'.");
    }
}
