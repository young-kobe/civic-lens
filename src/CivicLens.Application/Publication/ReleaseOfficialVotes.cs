using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Writes the vote files of the officials. A file changes only when this publish adds or replaces a vote that names its
/// official, now or in the replaced revision. Every other file is a hard link to the file of the previous release.
/// </summary>
internal sealed class ReleaseOfficialVotes(IReleaseDirectory releases, VoteRecordLoader votes)
{
    public async Task<IReadOnlyDictionary<string, ImmutableArray<PublishedOfficialVote>>> WriteAsync(IReleaseStaging staging,
        PublishedOfficials officials, IReadOnlyList<PendingRecord> additions, PreviousRelease previous, CancellationToken cancellationToken)
    {
        var added = additions.Select(addition => addition.Record).OfType<PublishedVote>().ToArray();
        var changes = await ChangesAsync(added, previous, cancellationToken);
        var addedIds = added.Select(vote => vote.RecordId).ToHashSet(StringComparer.Ordinal);
        var votesByOfficial = new Dictionary<string, ImmutableArray<PublishedOfficialVote>>(StringComparer.Ordinal);
        foreach (var official in officials.Officials.Where(official => PublicationProtocol.IsPathSegment(official.Id)))
        {
            var rows = await WriteOfficialAsync(staging, official.Id, changes, addedIds, previous, cancellationToken);
            if (!rows.IsEmpty) votesByOfficial[official.Id] = rows;
        }
        return votesByOfficial;
    }

    private async Task<ImmutableArray<PublishedOfficialVote>> WriteOfficialAsync(IReleaseStaging staging, string officialId,
        Dictionary<string, List<PublishedOfficialVote>> changes, HashSet<string> addedIds, PreviousRelease previous,
        CancellationToken cancellationToken)
    {
        var existing = previous.DirectoryName is null ? []
            : await releases.ListFilesAsync(previous.DirectoryName, PublicationProtocol.OfficialVotesFolder(officialId), cancellationToken);
        var changedPaths = changes.Keys.Where(path => path.StartsWith(PublicationProtocol.OfficialVotesFolder(officialId) + "/", StringComparison.Ordinal));
        var rows = ImmutableArray.CreateBuilder<PublishedOfficialVote>();
        foreach (var path in existing.Union(changedPaths, StringComparer.Ordinal))
        {
            var kept = existing.Contains(path) ? await ReadAsync(previous, path, cancellationToken) : [];
            rows.AddRange(changes.TryGetValue(path, out var changed)
                ? await RewriteAsync(staging, path, kept, changed, addedIds, cancellationToken)
                : await CarryAsync(staging, previous, path, kept, cancellationToken));
        }
        return rows.ToImmutable();
    }

    private static async Task<PublishedOfficialVote[]> CarryAsync(IReleaseStaging staging, PreviousRelease previous, string path,
        PublishedOfficialVote[] kept, CancellationToken cancellationToken)
    {
        await staging.LinkFileAsync(previous.DirectoryName!, path, cancellationToken);
        return kept;
    }

    private static async Task<PublishedOfficialVote[]> RewriteAsync(IReleaseStaging staging, string path, PublishedOfficialVote[] kept,
        List<PublishedOfficialVote> changed, HashSet<string> addedIds, CancellationToken cancellationToken)
    {
        var file = PublishedOfficialVotes.Of(kept.Where(vote => !addedIds.Contains(vote.RecordId)).Concat(changed));
        if (file.Votes.Length == 0) return [];
        await staging.WriteFileAsync(path, JsonSerializer.SerializeToUtf8Bytes(file, PublicationProtocol.JsonOptions), cancellationToken);
        return file.Votes;
    }

    private async Task<PublishedOfficialVote[]> ReadAsync(PreviousRelease previous, string path, CancellationToken cancellationToken)
    {
        var json = await releases.ReadFileAsync(previous.DirectoryName!, path, PublishedOfficialVotes.MaximumBytes, cancellationToken);
        var file = JsonSerializer.Deserialize<PublishedOfficialVotes>(json, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("An official vote file is empty.");
        file.Validate();
        return file.Votes;
    }

    private async Task<Dictionary<string, List<PublishedOfficialVote>>> ChangesAsync(PublishedVote[] added, PreviousRelease previous,
        CancellationToken cancellationToken)
    {
        var changes = new Dictionary<string, List<PublishedOfficialVote>>(StringComparer.Ordinal);
        foreach (var vote in added)
            foreach (var member in vote.Members)
                ChangesOf(changes, member.Official.Id, vote.Congress).Add(PublishedOfficialVote.Of(vote, member));

        var congressOf = added.ToDictionary(vote => vote.RecordId, vote => vote.Congress, StringComparer.Ordinal);
        var replaced = previous.Entries.Where(entry => entry.Kind == PublishedRecordKind.Vote && congressOf.ContainsKey(entry.RecordId));
        foreach (var congress in replaced.GroupBy(entry => congressOf[entry.RecordId]))
            foreach (var officialId in await votes.MemberPersonIdsAsync(congress, cancellationToken))
                ChangesOf(changes, officialId, congress.Key);
        return changes;
    }

    private static List<PublishedOfficialVote> ChangesOf(Dictionary<string, List<PublishedOfficialVote>> changes, string officialId, int congress)
    {
        var path = PublicationProtocol.OfficialVotesPath(officialId, congress);
        if (!changes.TryGetValue(path, out var rows)) changes[path] = rows = [];
        return rows;
    }
}
