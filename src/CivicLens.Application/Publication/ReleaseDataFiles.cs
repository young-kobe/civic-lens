using System.Text.Json;
using CivicLens.Application.People;
using CivicLens.Application.Roster;
using CivicLens.Application.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Writes the data files built from the manifest: the officials with a published record and their photos.
/// It also tells whether a profile differs from the previous release, which alone can make a release worth publishing.
/// Only the photos of those officials are loaded, so a configured person with no published record causes no capture read.
/// </summary>
internal sealed class ReleaseDataFiles(IReleaseDirectory releases, VoteRecordLoader votes, PublicationOfficials officials, ReleasePhotos photos)
{
    public async Task<ResolvedOfficials> WriteAsync(IReleaseStaging staging, PublicationRelease manifest,
        IReadOnlyList<PendingRecord> additions, PreviousRelease previous, IReadOnlyList<RosterMember> roster,
        IReadOnlyDictionary<string, PersonSetting> settings, IReadOnlyList<OfficialPhoto> photoRows, CancellationToken cancellationToken)
    {
        var members = await votes.MembersAsync(manifest.Records, cancellationToken);
        var entryNames = manifest.Records.SelectMany(entry => entry.Officials).DistinctBy(official => official.Id)
            .ToDictionary(official => official.Id, official => official.Name, StringComparer.Ordinal);
        var ids = entryNames.Keys.Concat(members.Select(member => member.PersonId)).ToHashSet(StringComparer.Ordinal);
        var recordNames = await RecordNamesAsync(manifest, ids, entryNames, members, additions, previous, cancellationToken);
        var photoFiles = await photos.LoadAsync([.. photoRows.Where(row => ids.Contains(row.PersonId))], previous, cancellationToken);
        var resolved = await officials.ResolveAsync(ids, recordNames, roster, settings, photoFiles.Profiles, cancellationToken);
        var published = resolved.Published;
        var officialsJson = JsonSerializer.SerializeToUtf8Bytes(published, PublicationProtocol.JsonOptions);
        if (officialsJson.Length > PublishedOfficials.MaximumBytes)
            throw new ArgumentException("The officials file would exceed its size limit.", nameof(manifest));
        await staging.WriteFileAsync(PublicationProtocol.OfficialsPath, officialsJson, cancellationToken);
        await photos.WriteAsync(staging, published, photoFiles, previous, cancellationToken);
        return resolved;
    }

    public static bool ChangesProfiles(PreviousRelease previous, PublishedOfficials published) =>
        published.Officials.Length != previous.Officials.Count ||
        published.Officials.Any(profile => !SameProfile(previous.Officials.GetValueOrDefault(profile.Id), profile));

    private static bool SameProfile(PublishedOfficialProfile? earlier, PublishedOfficialProfile profile) =>
        earlier is not null && JsonSerializer.SerializeToUtf8Bytes(earlier, PublicationProtocol.JsonOptions)
            .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(profile, PublicationProtocol.JsonOptions));

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
}
