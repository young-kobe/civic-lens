using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Writes the vote files and pages of the officials, and the page that lists the officials. A publish rebuilds the files of an
/// official only when the official is touched, and links every other file from the previous release. An official who left the configuration is always touched. The current Congress of an
/// official is the newest Congress with a vote file of that official. A change to a bill or a vote also touches the speakers of the links that cite it.
/// </summary>
internal sealed class ReleaseOfficialPages(ReleaseOfficialVotes votes, PublicationOfficials officials, IReleaseRenderer renderer,
    PublicationCatalog catalog)
{
    public async Task WriteAsync(IReleaseStaging staging, PublicationRelease manifest, PublishedOfficials published,
        IReadOnlyList<PendingRecord> additions, IReadOnlyList<PublishedRecordEntry> removed, PreviousRelease previous,
        CancellationToken cancellationToken)
    {
        var release = manifest.Records.ToDictionary(entry => (entry.Kind, entry.RecordId));
        var changes = await votes.ChangesAsync(additions, removed, previous, cancellationToken);
        var entriesByOfficial = manifest.Records.SelectMany(entry => entry.Officials.Select(official => (official.Id, Entry: entry)))
            .ToLookup(pair => pair.Id, pair => pair.Entry, StringComparer.Ordinal);
        var namedByChange = OfficialsNamedByChanges(manifest, additions, removed, previous);
        var listings = new List<OfficialListing>();
        foreach (var profile in published.Officials)
        {
            ImmutableArray<PublishedRecordEntry> entries = [.. entriesByOfficial[profile.Id]];
            listings.Add(new(profile, entries.Length));
            previous.Officials.TryGetValue(profile.Id, out var earlier);
            var tracked = officials.IsConfigured(profile.Id);
            if (!tracked || !SameProfile(earlier, profile) || namedByChange.Contains(profile.Id) || changes.Touches(profile.Id))
                await WriteTouchedAsync(staging, profile, tracked, entries, release, changes, previous, earlier?.Name != profile.Name, cancellationToken);
            else
                await LinkAsync(staging, profile.Id, previous, cancellationToken);
        }
        await WriteAsync(staging, PublicationProtocol.OfficialsPagePath, await renderer.RenderOfficialsAsync(listings, cancellationToken),
            cancellationToken);
    }

    private static bool SameProfile(PublishedOfficialProfile? earlier, PublishedOfficialProfile profile) =>
        earlier is not null && JsonSerializer.SerializeToUtf8Bytes(earlier, PublicationProtocol.JsonOptions)
            .AsSpan().SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(profile, PublicationProtocol.JsonOptions));

    private static HashSet<string> OfficialsNamedByChanges(PublicationRelease manifest, IReadOnlyList<PendingRecord> additions,
        IReadOnlyList<PublishedRecordEntry> removed, PreviousRelease previous)
    {
        var added = additions.Select(addition => (addition.Entry.Kind, addition.RecordId)).ToHashSet();
        var linksByTarget = manifest.Records.Where(entry => entry.Kind == PublishedRecordKind.Relationship)
            .ToLookup(link => link.TargetRecordId, StringComparer.Ordinal);
        return [.. additions.Select(addition => addition.Entry)
            .Concat(previous.Entries.Where(entry => added.Contains((entry.Kind, entry.RecordId))))
            .Concat(removed)
            .SelectMany(entry => entry.Officials.Concat(linksByTarget[entry.RecordId].SelectMany(link => link.Officials)))
            .Select(official => official.Id)];
    }

    private async Task LinkAsync(IReleaseStaging staging, string officialId, PreviousRelease previous, CancellationToken cancellationToken)
    {
        var files = await votes.ListAsync(previous, officialId, cancellationToken);
        await Link(PublicationProtocol.OfficialPagePath(officialId));
        foreach (var congress in files.VoteCongresses) await Link(PublicationProtocol.OfficialVotesPath(officialId, congress));
        foreach (var congress in files.PageCongresses) await Link(PublicationProtocol.OfficialCongressPagePath(officialId, congress));

        Task Link(string path) => staging.LinkFileAsync(previous.DirectoryName!, path, cancellationToken);
    }

    private async Task WriteTouchedAsync(IReleaseStaging staging, PublishedOfficialProfile profile, bool tracked,
        ImmutableArray<PublishedRecordEntry> entries, IReadOnlyDictionary<(PublishedRecordKind, string), PublishedRecordEntry> release,
        OfficialVoteChanges changes, PreviousRelease previous, bool nameChanged,
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
                    new(profile, tracked, congress, false, [], [], await RowsAsync(congress)), catalog, cancellationToken), cancellationToken);
            else
                await staging.LinkFileAsync(previous.DirectoryName!, path, cancellationToken);
        }

        var current = newestFirst.IsEmpty ? (int?)null : newestFirst[0];
        var currentRows = current is { } newest ? await RowsAsync(newest) : [];
        var talkAndAction = current is null ? null : TalkAndActionProfile.Of(current.Value, entries, currentRows);
        await WriteAsync(staging, PublicationProtocol.OfficialPagePath(profile.Id), await renderer.RenderOfficialAsync(
            new(profile, tracked, current, true, past, entries, currentRows, talkAndAction,
                current is { } newestCongress ? LinkedPair.In(newestCongress, entries, release, currentRows) : []), catalog, cancellationToken), cancellationToken);
    }

    private static Task WriteAsync(IReleaseStaging staging, string path, string html, CancellationToken cancellationToken) =>
        staging.WriteFileAsync(path, Encoding.UTF8.GetBytes(html), cancellationToken);
}
