using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Writes the vote files and pages of the officials, and the page that lists the officials. A publish rebuilds the files of an
/// official only when the official is touched or the operator asks for a rewrite of all officials, and links every other file from the previous release. It also writes the summary row of each official: new for a touched official, carried for the others. An official who left the configuration is always touched. The current Congress of an
/// official is the newest Congress with a vote file of that official. A change to a bill or a vote also touches the matched official of each link that cites it.
/// </summary>
internal sealed class ReleaseOfficialPages(ReleaseOfficialVotes votes, PublicationOfficials officials, IReleaseRenderer renderer,
    PublicationCatalog catalog)
{
    public async Task<(IReadOnlyList<OfficialListing> Listings, IReadOnlyList<PublishedOfficialSummary> Summaries)> WriteAsync(IReleaseStaging staging, PublicationRelease manifest, PublishedOfficials published,
        IReadOnlyList<PendingRecord> additions, IReadOnlyList<PublishedRecordEntry> removed, PreviousRelease previous,
        bool rewriteAll, CancellationToken cancellationToken)
    {
        var release = manifest.Records.ToDictionary(entry => (entry.Kind, entry.RecordId));
        var changes = await votes.ChangesAsync(additions, removed, previous, cancellationToken);
        var entriesByOfficial = manifest.Records.SelectMany(entry => entry.Officials.Select(official => (official.Id, Entry: entry)))
            .ToLookup(pair => pair.Id, pair => pair.Entry, StringComparer.Ordinal);
        var namedByChange = OfficialsNamedByChanges(manifest, additions, removed, previous);
        var cosponsored = ReleaseOfficialSummaries.CosponsoredCounts(manifest.Records);
        var listings = new List<OfficialListing>();
        var summaries = new List<PublishedOfficialSummary>();
        foreach (var profile in published.Officials)
        {
            ImmutableArray<PublishedRecordEntry> entries = [.. entriesByOfficial[profile.Id]];
            listings.Add(new(profile, entries.Length));
            previous.Officials.TryGetValue(profile.Id, out var earlier);
            var tracked = officials.IsConfigured(profile.Id);
            PublishedOfficialSummary summary;
            if (rewriteAll || !tracked || !SameProfile(earlier, profile) || namedByChange.Contains(profile.Id) || changes.Touches(profile.Id))
            {
                summary = await WriteTouchedAsync(staging, profile, tracked, entries, release, changes, previous, rewriteAll || earlier?.Name != profile.Name, manifest.PublishedAtUtc, cancellationToken);
            }
            else
            {
                await LinkAsync(staging, profile.Id, previous, cancellationToken);
                summary = previous.Summaries.GetValueOrDefault(profile.Id) ?? PublishedOfficialSummary.Empty(profile.Id);
            }
            summaries.Add(summary with { CosponsoredBills = cosponsored?.GetValueOrDefault(profile.Id) });
        }
        await ReleaseOfficialSummaries.WriteAsync(staging, summaries, cancellationToken);
        await WriteAsync(staging, PublicationProtocol.OfficialsPagePath, await renderer.RenderOfficialsAsync(listings, manifest.PublishedAtUtc, cancellationToken),
            cancellationToken);
        return (listings, summaries);
    }

    public static bool ChangesProfiles(PreviousRelease previous, PublishedOfficials published) =>
        published.Officials.Length != previous.Officials.Count ||
        published.Officials.Any(profile => !SameProfile(previous.Officials.GetValueOrDefault(profile.Id), profile));

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
            .SelectMany(entry => entry.Officials.Select(official => official.Id).Concat(linksByTarget[entry.RecordId].Select(link => link.MatchedOfficialId!)))];
    }

    private async Task LinkAsync(IReleaseStaging staging, string officialId, PreviousRelease previous, CancellationToken cancellationToken)
    {
        var files = await votes.ListAsync(previous, officialId, cancellationToken);
        await Link(PublicationProtocol.OfficialPagePath(officialId));
        foreach (var congress in files.VoteCongresses) await Link(PublicationProtocol.OfficialVotesPath(officialId, congress));
        foreach (var congress in files.PageCongresses) await Link(PublicationProtocol.OfficialCongressPagePath(officialId, congress));

        Task Link(string path) => staging.LinkFileAsync(previous.DirectoryName!, path, cancellationToken);
    }

    private async Task<PublishedOfficialSummary> WriteTouchedAsync(IReleaseStaging staging, PublishedOfficialProfile profile, bool tracked,
        ImmutableArray<PublishedRecordEntry> entries, IReadOnlyDictionary<(PublishedRecordKind, string), PublishedRecordEntry> release,
        OfficialVoteChanges changes, PreviousRelease previous, bool rewritePast, DateTimeOffset publishedAtUtc,
        CancellationToken cancellationToken)
    {
        var files = await votes.ListAsync(previous, profile.Id, cancellationToken);
        var rowsInMemory = new Dictionary<int, PublishedOfficialVote[]>();
        var congresses = new List<int>();
        var changed = changes.Of(profile.Id);
        foreach (var congress in files.VoteCongresses.Union(changed.Keys).Order())
        {
            if (changed.TryGetValue(congress, out var added))
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

        var congressesChanged = !files.VoteCongresses.SetEquals(congresses);
        foreach (var congress in newestFirst.Skip(1))
        {
            var path = PublicationProtocol.OfficialCongressPagePath(profile.Id, congress);
            if (rewritePast || congressesChanged || rowsInMemory.ContainsKey(congress))
                await WriteAsync(staging, path, await renderer.RenderOfficialAsync(
                    new(profile, tracked, congress, false, newestFirst, [], await RowsAsync(congress), publishedAtUtc), catalog, cancellationToken), cancellationToken);
            else
                await staging.LinkFileAsync(previous.DirectoryName!, path, cancellationToken);
        }

        var current = newestFirst.IsEmpty ? (int?)null : newestFirst[0];
        var currentRows = current is { } newest ? await RowsAsync(newest) : [];
        var talkAndAction = current is null ? null : TalkAndActionProfile.Of(current.Value, entries, currentRows);
        await WriteAsync(staging, PublicationProtocol.OfficialPagePath(profile.Id), await renderer.RenderOfficialAsync(
            new(profile, tracked, current, true, newestFirst, entries, currentRows, publishedAtUtc, talkAndAction,
                current is { } newestCongress ? LinkedPair.In(profile.Id, newestCongress, entries, release, currentRows) : []), catalog, cancellationToken), cancellationToken);
        return ReleaseOfficialSummaries.Of(profile.Id, current, talkAndAction);
    }

    private static Task WriteAsync(IReleaseStaging staging, string path, string html, CancellationToken cancellationToken) =>
        staging.WriteFileAsync(path, Encoding.UTF8.GetBytes(html), cancellationToken);
}
