using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Finds, reads, and rewrites the vote files of the officials. The rows of a publish come from the vote records that it holds.
/// A replaced or withdrawn vote leaves the files of its members, and a Congress with no rows leaves with it.
/// </summary>
internal sealed class ReleaseOfficialVotes(IReleaseDirectory releases, VoteRecordLoader votes)
{
    public async Task<OfficialVoteChanges> ChangesAsync(IReadOnlyList<PendingRecord> additions,
        IReadOnlyList<PublishedRecordEntry> removed, PreviousRelease previous, CancellationToken cancellationToken)
    {
        var added = additions.OfType<PendingRecord<PublishedVote>>().Select(addition => addition.Record).ToArray();
        var changes = new Dictionary<string, Dictionary<int, List<PublishedOfficialVote>>>(StringComparer.Ordinal);
        foreach (var vote in added)
            foreach (var member in vote.Members)
                RowsOf(changes, member.Official.Id, vote.Congress).Add(PublishedOfficialVote.Of(vote, member));

        var congressOf = added.ToDictionary(vote => vote.RecordId, vote => vote.Congress, StringComparer.Ordinal);
        foreach (var entry in removed.Where(entry => entry.Kind == PublishedRecordKind.Vote))
            congressOf[entry.RecordId] = await ReadCongressAsync(previous, entry.RecordId, cancellationToken);
        var replaced = previous.Entries.Where(entry => entry.Kind == PublishedRecordKind.Vote && congressOf.ContainsKey(entry.RecordId)).ToArray();
        if (replaced.Length > 0)
        {
            var congresses = replaced.Select(entry => congressOf[entry.RecordId]).Distinct().ToArray();
            foreach (var officialId in (await votes.MembersAsync(replaced, cancellationToken)).Select(member => member.PersonId).Distinct())
                foreach (var congress in congresses) RowsOf(changes, officialId, congress);
        }
        return new(changes, [.. congressOf.Keys]);
    }

    private async Task<int> ReadCongressAsync(PreviousRelease previous, string recordId, CancellationToken cancellationToken)
    {
        var json = await releases.ReadFileAsync(previous.DirectoryName!, PublicationProtocol.RecordDataPath(recordId),
            PublishedRecordFiles.MaximumCitedRecordBytes, cancellationToken);
        return (JsonSerializer.Deserialize<VoteCongress>(json, PublishedRecordFiles.PartialReadOptions)
            ?? throw new InvalidDataException("A carried vote file is empty.")).Congress;
    }

    public async Task<OfficialFiles> ListAsync(PreviousRelease previous, string officialId, CancellationToken cancellationToken)
    {
        if (previous.DirectoryName is null) return OfficialFiles.None;
        var paths = await releases.ListFilesAsync(previous.DirectoryName, PublicationProtocol.OfficialFolder(officialId),
            PublicationProtocol.MaximumOfficialFiles, cancellationToken);
        return new([.. paths.Select(PublicationProtocol.VotesCongressOf).OfType<int>()],
            [.. paths.Select(PublicationProtocol.CongressPageOf).OfType<int>()]);
    }

    public async Task<PublishedOfficialVote[]> ReadAsync(PreviousRelease previous, string officialId, int congress,
        CancellationToken cancellationToken)
    {
        var json = await releases.ReadFileAsync(previous.DirectoryName!, PublicationProtocol.OfficialVotesPath(officialId, congress),
            PublishedOfficialVotes.MaximumBytes, cancellationToken);
        var file = JsonSerializer.Deserialize<PublishedOfficialVotes>(json, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("An official vote file is empty.");
        file.Validate();
        if (file.Votes.Any(vote => vote.Congress != congress))
            throw new InvalidDataException("An official vote file holds a vote of another Congress.");
        return file.Votes;
    }

    public async Task<PublishedOfficialVote[]> RewriteAsync(IReleaseStaging staging, string officialId, int congress,
        PublishedOfficialVote[] kept, List<PublishedOfficialVote> added, OfficialVoteChanges changes, CancellationToken cancellationToken)
    {
        var file = PublishedOfficialVotes.Of(kept.Where(vote => !changes.Replaces(vote.RecordId)).Concat(added));
        if (file.Votes.Length == 0) return [];
        await staging.WriteFileAsync(PublicationProtocol.OfficialVotesPath(officialId, congress),
            JsonSerializer.SerializeToUtf8Bytes(file, PublicationProtocol.JsonOptions), cancellationToken);
        return file.Votes;
    }

    private sealed record VoteCongress(int Congress);

    private static List<PublishedOfficialVote> RowsOf(Dictionary<string, Dictionary<int, List<PublishedOfficialVote>>> changes,
        string officialId, int congress)
    {
        if (!changes.TryGetValue(officialId, out var byCongress)) changes[officialId] = byCongress = [];
        if (!byCongress.TryGetValue(congress, out var rows)) byCongress[congress] = rows = [];
        return rows;
    }
}
