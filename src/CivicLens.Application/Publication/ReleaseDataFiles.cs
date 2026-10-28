using System.Text.Json;
using CivicLens.Application.Roster;
using CivicLens.Application.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Writes the data files built from the manifest: the officials with a published record, and the issue and policy area names.</summary>
internal sealed class ReleaseDataFiles(IReleaseDirectory releases, VoteRecordLoader votes, PublicationOfficials officials)
{
    public async Task<PublishedOfficials> WriteAsync(IReleaseStaging staging, PublicationRelease manifest, ReleaseFacets facets,
        IReadOnlyList<PendingRecord> additions, PreviousRelease previous, IReadOnlyList<RosterMember> roster,
        IReadOnlyDictionary<string, PublishedPhoto> photos, CancellationToken cancellationToken)
    {
        var members = await votes.MembersAsync(manifest.Records, cancellationToken);
        var entryNames = manifest.Records.SelectMany(entry => entry.Officials).DistinctBy(official => official.Id)
            .ToDictionary(official => official.Id, official => official.Name, StringComparer.Ordinal);
        var ids = entryNames.Keys.Concat(members.Select(member => member.PersonId)).ToHashSet(StringComparer.Ordinal);
        var recordNames = await RecordNamesAsync(manifest, ids, entryNames, members, additions, previous, cancellationToken);
        var published = await officials.ResolveAsync(ids, recordNames, roster, photos, cancellationToken);
        var officialsJson = JsonSerializer.SerializeToUtf8Bytes(published, PublicationProtocol.JsonOptions);
        if (officialsJson.Length > PublishedOfficials.MaximumBytes)
            throw new ArgumentException("The officials file would exceed its size limit.", nameof(manifest));
        await staging.WriteFileAsync(PublicationProtocol.OfficialsPath, officialsJson, cancellationToken);
        await WriteAsync(staging, PublicationProtocol.CatalogPath, CatalogOf(facets), cancellationToken);
        return published;
    }

    // Only an official who left the configuration needs a name from the records. A vote file gives the name of a vote-only member.
    private async Task<Dictionary<string, string>> RecordNamesAsync(PublicationRelease manifest, HashSet<string> ids,
        Dictionary<string, string> entryNames, IReadOnlyList<VoteMembership> members, IReadOnlyList<PendingRecord> additions,
        PreviousRelease previous, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in ids.Where(id => !officials.IsConfigured(id)))
        {
            var name = entryNames.GetValueOrDefault(id);
            if (name is null)
            {
                var entry = manifest.Records.First(entry => entry.Kind == PublishedRecordKind.Vote &&
                    entry.RecordId == members.First(member => member.PersonId == id).RecordId);
                var vote = await ReleaseRecordFiles.ReadAsync<PublishedVote>(releases, entry, additions, previous, cancellationToken);
                name = vote.Members.FirstOrDefault(member => member.Official.Id == id)?.Official.Name;
            }
            if (name is not null) names[id] = name;
        }
        return names;
    }

    private static PublishedCatalog CatalogOf(ReleaseFacets facets)
    {
        var catalog = new PublishedCatalog { Issues = EntriesOf(facets.Issues), PolicyAreas = EntriesOf(facets.PolicyAreas) };
        catalog.Validate();
        return catalog;
    }

    private static PublishedCatalogEntry[] EntriesOf(IEnumerable<ReleaseFacet> facets) =>
        [.. facets.Select(facet => new PublishedCatalogEntry { Id = facet.Id, Name = facet.Name })];

    private static Task WriteAsync<TFile>(IReleaseStaging staging, string path, TFile file, CancellationToken cancellationToken) =>
        staging.WriteFileAsync(path, JsonSerializer.SerializeToUtf8Bytes(file, PublicationProtocol.JsonOptions), cancellationToken);
}
