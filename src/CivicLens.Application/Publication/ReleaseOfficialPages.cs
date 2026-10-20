using System.Collections.Immutable;
using System.Text;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Writes the vote files and pages of the officials, and the page that lists the officials. A publish rebuilds the files of an
/// official only when the official is touched, and links every other file from the previous release. The current Congress of an
/// official is the newest Congress with a vote file of that official.
/// </summary>
internal sealed class ReleaseOfficialPages(ReleaseOfficialVotes votes, IReleaseRenderer renderer)
{
    public async Task WriteAsync(IReleaseStaging staging, PublicationRelease manifest, PublishedOfficials officials,
        IReadOnlyList<PendingRecord> additions, PreviousRelease previous, CancellationToken cancellationToken)
    {
        var changes = await votes.ChangesAsync(additions, previous, cancellationToken);
        var entriesByOfficial = manifest.Records.SelectMany(entry => entry.Officials.Select(official => (official.Id, Entry: entry)))
            .ToLookup(pair => pair.Id, pair => pair.Entry, StringComparer.Ordinal);
        var namedByChange = OfficialsNamedByChanges(additions, previous);
        var listings = new List<OfficialListing>();
        foreach (var profile in officials.Officials)
        {
            ImmutableArray<PublishedRecordEntry> entries = [.. entriesByOfficial[profile.Id]];
            listings.Add(new(profile, entries.Length));
            previous.Officials.TryGetValue(profile.Id, out var earlier);
            if (!profile.Equals(earlier) || namedByChange.Contains(profile.Id) || changes.Touches(profile.Id))
                await WriteTouchedAsync(staging, profile, entries, changes, previous, earlier?.Name != profile.Name, cancellationToken);
            else
                await LinkAsync(staging, profile.Id, previous, cancellationToken);
        }
        await WriteAsync(staging, PublicationProtocol.OfficialsPagePath, await renderer.RenderOfficialsAsync(listings, cancellationToken),
            cancellationToken);
    }

    private static HashSet<string> OfficialsNamedByChanges(IReadOnlyList<PendingRecord> additions, PreviousRelease previous)
    {
        var added = additions.Select(addition => (addition.Entry.Kind, addition.RecordId)).ToHashSet();
        return [.. additions.Select(addition => addition.Entry)
            .Concat(previous.Entries.Where(entry => added.Contains((entry.Kind, entry.RecordId))))
            .SelectMany(entry => entry.Officials).Select(official => official.Id)];
    }

    private async Task LinkAsync(IReleaseStaging staging, string officialId, PreviousRelease previous, CancellationToken cancellationToken)
    {
        var files = await votes.ListAsync(previous, officialId, cancellationToken);
        await Link(PublicationProtocol.OfficialPagePath(officialId));
        foreach (var congress in files.VoteCongresses) await Link(PublicationProtocol.OfficialVotesPath(officialId, congress));
        foreach (var congress in files.PageCongresses) await Link(PublicationProtocol.OfficialCongressPagePath(officialId, congress));

        Task Link(string path) => staging.LinkFileAsync(previous.DirectoryName!, path, cancellationToken);
    }

    private async Task WriteTouchedAsync(IReleaseStaging staging, PublishedOfficialProfile profile,
        ImmutableArray<PublishedRecordEntry> entries, OfficialVoteChanges changes, PreviousRelease previous, bool nameChanged,
        CancellationToken cancellationToken)
    {
        var files = await votes.ListAsync(previous, profile.Id, cancellationToken);
        var rowsInMemory = new Dictionary<int, PublishedOfficialVote[]>();
        var congresses = new List<int>();
        foreach (var congress in files.VoteCongresses.Union(changes.Of(profile.Id).Keys).Order())
        {
            if (changes.Of(profile.Id).TryGetValue(congress, out var added))
            {
                var kept = files.VoteCongresses.Contains(congress) ? await votes.ReadAsync(previous, profile.Id, congress, cancellationToken) : [];
                var rows = await votes.RewriteAsync(staging, profile.Id, congress, kept, added, changes, cancellationToken);
                if (rows.Length == 0) continue;
                rowsInMemory[congress] = rows;
            }
            else
            {
                await staging.LinkFileAsync(previous.DirectoryName!, PublicationProtocol.OfficialVotesPath(profile.Id, congress), cancellationToken);
            }
            congresses.Add(congress);
        }

        ImmutableArray<int> newestFirst = [.. congresses.OrderDescending()];
        async Task<ImmutableArray<PublishedOfficialVote>> RowsAsync(int congress) =>
            [.. rowsInMemory.TryGetValue(congress, out var rows) ? rows : await votes.ReadAsync(previous, profile.Id, congress, cancellationToken)];

        ImmutableArray<int> past = [.. newestFirst.Skip(1)];
        foreach (var congress in past)
        {
            var path = PublicationProtocol.OfficialCongressPagePath(profile.Id, congress);
            if (nameChanged || rowsInMemory.ContainsKey(congress) || !files.PageCongresses.Contains(congress))
                await WriteAsync(staging, path, await renderer.RenderOfficialAsync(
                    new(profile, congress, false, [], [], await RowsAsync(congress)), cancellationToken), cancellationToken);
            else
                await staging.LinkFileAsync(previous.DirectoryName!, path, cancellationToken);
        }

        var current = newestFirst.IsEmpty ? (int?)null : newestFirst[0];
        var currentRows = current is { } newest ? await RowsAsync(newest) : [];
        await WriteAsync(staging, PublicationProtocol.OfficialPagePath(profile.Id), await renderer.RenderOfficialAsync(
            new(profile, current, true, past, entries, currentRows), cancellationToken), cancellationToken);
    }

    private static Task WriteAsync(IReleaseStaging staging, string path, string html, CancellationToken cancellationToken) =>
        staging.WriteFileAsync(path, Encoding.UTF8.GetBytes(html), cancellationToken);
}
