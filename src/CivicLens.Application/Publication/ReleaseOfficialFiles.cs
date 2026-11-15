using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Writes the vote files and the summary row of each official. A publish rebuilds the files of an official only when its votes change, when the previous
/// release holds no summary row for the official, or when the operator asks for a rewrite of all officials. It links every other vote file from the previous
/// release and carries the summary row. The current Congress of an official is the newest Congress with a vote file of that official.
/// </summary>
internal sealed class ReleaseOfficialFiles(ReleaseOfficialVotes votes)
{
    public async Task WriteAsync(IReleaseStaging staging, PublicationRelease manifest, PublishedOfficials published,
        IReadOnlyList<PendingRecord> additions, IReadOnlyList<PublishedRecordEntry> removed, PreviousRelease previous, bool rewriteAll,
        CancellationToken cancellationToken)
    {
        var changes = await votes.ChangesAsync(additions, removed, previous, cancellationToken);
        var cosponsored = ReleaseOfficialSummaries.CosponsoredCounts(manifest.Records);
        var summaries = new List<PublishedOfficialSummary>();
        foreach (var profile in published.Officials)
        {
            var congresses = await votes.ListAsync(previous, profile.Id, cancellationToken);
            var carried = previous.Summaries.GetValueOrDefault(profile.Id);
            var summary = rewriteAll || carried is null || changes.Touches(profile.Id)
                ? await WriteTouchedAsync(staging, profile.Id, changes, previous, congresses, rewriteAll, cancellationToken)
                : await LinkAsync(staging, profile.Id, previous, congresses, carried, cancellationToken);
            summaries.Add(summary with { CosponsoredBills = cosponsored?.GetValueOrDefault(profile.Id) });
        }
        await ReleaseOfficialSummaries.WriteAsync(staging, summaries, cancellationToken);
    }

    public static bool ChangesProfiles(PreviousRelease previous, PublishedOfficials published) =>
        published.Officials.Length != previous.Officials.Count ||
        published.Officials.Any(profile => !SameProfile(previous.Officials.GetValueOrDefault(profile.Id), profile));

    private static bool SameProfile(PublishedOfficialProfile? earlier, PublishedOfficialProfile profile) =>
        earlier is not null && JsonSerializer.SerializeToUtf8Bytes(earlier, PublicationProtocol.JsonOptions)
            .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(profile, PublicationProtocol.JsonOptions));

    private static async Task<PublishedOfficialSummary> LinkAsync(IReleaseStaging staging, string officialId, PreviousRelease previous,
        HashSet<int> congresses, PublishedOfficialSummary carried, CancellationToken cancellationToken)
    {
        foreach (var congress in congresses)
            await staging.LinkFileAsync(previous.DirectoryName!, PublicationProtocol.OfficialVotesPath(officialId, congress), cancellationToken);
        return carried;
    }

    private async Task<PublishedOfficialSummary> WriteTouchedAsync(IReleaseStaging staging, string officialId, OfficialVoteChanges changes,
        PreviousRelease previous, HashSet<int> previousCongresses, bool rewriteAll, CancellationToken cancellationToken)
    {
        var rowsInMemory = new Dictionary<int, PublishedOfficialVote[]>();
        var congresses = new List<int>();
        var changed = changes.Of(officialId);
        foreach (var congress in previousCongresses.Union(changed.Keys).Order())
        {
            if (changed.TryGetValue(congress, out var added))
            {
                var kept = previousCongresses.Contains(congress) ? await votes.ReadAsync(previous, officialId, congress, cancellationToken) : [];
                var rows = await votes.RewriteAsync(staging, officialId, congress, kept, added, changes, cancellationToken);
                if (rows.Length == 0) continue;
                rowsInMemory[congress] = rows;
            }
            else
            {
                await staging.LinkFileAsync(previous.DirectoryName!, PublicationProtocol.OfficialVotesPath(officialId, congress), cancellationToken);
            }
            congresses.Add(congress);
        }

        var newestFirst = congresses.OrderDescending().ToArray();
        if (newestFirst.Length == 0) return PublishedOfficialSummary.Empty(officialId);
        var current = ReleaseOfficialSummaries.FiguresOf(newestFirst[0], await RowsAsync(newestFirst[0]));
        var previousFigures = newestFirst.Length > 1 ? await PreviousFiguresAsync(newestFirst[1]) : null;
        return ReleaseOfficialSummaries.Of(officialId, current, previousFigures);

        async Task<PublishedOfficialVote[]> RowsAsync(int congress) =>
            rowsInMemory.TryGetValue(congress, out var rows) ? rows : await votes.ReadAsync(previous, officialId, congress, cancellationToken);

        async Task<PublishedVoteFigures> PreviousFiguresAsync(int older)
        {
            var unchanged = !rewriteAll && !rowsInMemory.ContainsKey(older);
            if (unchanged && previous.Summaries.GetValueOrDefault(officialId)?.Previous is { } carried && carried.Congress == older) return carried;
            return ReleaseOfficialSummaries.FiguresOf(older, await RowsAsync(older));
        }
    }
}
