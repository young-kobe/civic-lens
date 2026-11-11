using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Fills the stored columns that C# writes at publication, for rows that earlier releases wrote. Each fill calls the rule of the commit writer.
/// A vote revision takes <see cref="VoteStoredColumns.From"/> of its own vote record file, which the newest kept folder in its release range holds. Its columns
/// and its written marker go in together. A member list pins the parties of its published revision with the lowest revision number that has a kept file, so a later revision of the
/// list never changes them. A revision with no kept file stays unwritten. An official takes the photo path of the officials file of its release. An entry takes
/// <see cref="ReleaseRecordOrder.CongressOf(DateOnly?, DateTimeOffset)"/>. A release takes the vote links of <see cref="ReleaseBillLines"/> over its manifest. Every write fills only what is not filled yet, so a second run changes nothing.
/// Work runs in batches of <see cref="BatchSize"/> rows.
/// </summary>
public sealed class BackfillStoredColumns(IPublicationStore publications, IReleaseDirectory releases, IVoteReviewStore votes)
{
    private const int BatchSize = 25;

    public async Task<StoredColumnsBackfillResult> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var releaseRows = (await publications.ListAllAsync(cancellationToken)).OrderByDescending(release => release.ReleaseNumber).ToArray();
        var revisions = await BackfillVotesAsync(releaseRows, cancellationToken);
        var photos = await BackfillPhotoPathsAsync(releaseRows, cancellationToken);
        var congresses = await BackfillCongressesAsync(cancellationToken);
        var voteBills = await BackfillVoteBillsAsync(releaseRows, cancellationToken);
        return new(revisions.Examined, revisions.Written, revisions.OpenWithoutFile, revisions.ClosedWithoutFile, photos, congresses, voteBills);
    }

    private async Task<(int Examined, int Written, int OpenWithoutFile, int ClosedWithoutFile)> BackfillVotesAsync(PublicationReleaseSummary[] releaseRows,
        CancellationToken cancellationToken)
    {
        var gaps = await publications.ReadVoteColumnGapsAsync(cancellationToken);
        var pinnedLists = new HashSet<string>(StringComparer.Ordinal);
        int written = 0, openWithoutFile = 0, closedWithoutFile = 0;
        foreach (var batch in gaps.Chunk(BatchSize))
        {
            var columns = new List<VoteStoredColumns>();
            foreach (var gap in batch)
            {
                if (await ReadColumnsAsync(releaseRows, gap, cancellationToken) is not { } found)
                {
                    if (gap.IsOpen) openWithoutFile++;
                    else closedWithoutFile++;
                    continue;
                }
                // The gaps come in revision order for each list, so the first revision with a file owns the parties of the list.
                columns.Add(pinnedLists.Add(gap.ListId) ? found : found with { MemberParties = [] });
            }
            await publications.WriteVoteColumnsAsync(columns, cancellationToken);
            written += columns.Count;
        }
        return (gaps.Count, written, openWithoutFile, closedWithoutFile);
    }

    private async Task<VoteStoredColumns?> ReadColumnsAsync(PublicationReleaseSummary[] releaseRows, VoteColumnGap gap, CancellationToken cancellationToken)
    {
        foreach (var release in releaseRows.Where(release => gap.Covers(release.ReleaseNumber)))
        {
            if (await ReadVoteAsync(release, gap, cancellationToken) is { } vote) return VoteStoredColumns.From(vote);
        }
        return null;
    }

    private async Task<PublishedVote?> ReadVoteAsync(PublicationReleaseSummary release, VoteColumnGap gap, CancellationToken cancellationToken)
    {
        try
        {
            var vote = ReleaseRecordFiles.Deserialize<PublishedVote>(await ReleaseRecordFiles.ReadAsync(releases, release.DirectoryName, gap.RecordId, cancellationToken));
            vote.Validate();
            return vote.RecordId == gap.RecordId && vote.RevisionNumber == gap.RevisionNumber ? vote : null;
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or JsonException)
        {
            return null;
        }
    }

    private async Task<int> BackfillPhotoPathsAsync(PublicationReleaseSummary[] releaseRows, CancellationToken cancellationToken)
    {
        var filled = 0;
        foreach (var release in releaseRows)
        {
            Dictionary<string, PublishedOfficialProfile> officials;
            try
            {
                officials = await PreviousRelease.ReadOfficialsAsync(releases, release.DirectoryName, cancellationToken);
            }
            catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or JsonException)
            {
                continue;
            }
            var paths = officials.Values.Where(official => official.Photo is not null).ToDictionary(official => official.Id, official => official.Photo!.Path, StringComparer.Ordinal);
            foreach (var batch in paths.Chunk(BatchSize))
                filled += await publications.FillOfficialPhotoPathsAsync(release.ReleaseNumber, batch.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal), cancellationToken);
        }
        return filled;
    }

    private async Task<int> BackfillCongressesAsync(CancellationToken cancellationToken)
    {
        var filled = 0;
        PublishedEntryDay? last = null;
        while (await publications.ReadEntryDaysWithoutCongressAsync(last, BatchSize, cancellationToken) is { Count: > 0 } batch)
        {
            filled += await publications.FillEntryCongressesAsync(batch, cancellationToken);
            last = batch[^1];
        }
        return filled;
    }

    private async Task<int> BackfillVoteBillsAsync(PublicationReleaseSummary[] releaseRows, CancellationToken cancellationToken)
    {
        var filled = 0;
        foreach (var release in releaseRows)
        {
            PublicationRelease manifest;
            try
            {
                manifest = await PreviousRelease.LoadManifestAsync(releases, release, cancellationToken);
            }
            catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException)
            {
                continue;
            }
            var voteBillActions = await votes.FindBillActionRecordIdsOfVotesAsync(
                [.. manifest.Records.Where(entry => entry.Kind == PublishedRecordKind.Vote).Select(PublishedRevisionBinding.Of)], cancellationToken);
            foreach (var batch in ReleaseBillLines.From(manifest.Records, voteBillActions).VoteBills().Chunk(BatchSize))
                filled += await publications.AddVoteBillsAsync(release.ReleaseNumber, batch, cancellationToken);
        }
        return filled;
    }
}
