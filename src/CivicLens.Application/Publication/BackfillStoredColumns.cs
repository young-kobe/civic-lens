using System.Text.Json;
using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Fills the stored columns that C# writes at publication, for rows that earlier releases wrote. Each fill calls the rule of the commit writer.
/// A vote revision takes <see cref="VoteStoredColumns.From"/> of the vote record file that the newest kept folder holds for that revision, so a member whose
/// roster term changed keeps the party that the release gave on the vote day. An official takes the photo path of the officials file of its release. An entry takes
/// <see cref="ReleaseRecordOrder.CongressOf(DateOnly?, DateTimeOffset)"/>. Every write fills only a column that holds none, so a second run changes nothing.
/// Work runs in batches of <see cref="BatchSize"/> rows.
/// </summary>
public sealed class BackfillStoredColumns(IPublicationStore publications, IReleaseDirectory releases)
{
    private const int BatchSize = 25;

    public async Task<StoredColumnsBackfillResult> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var keptReleases = (await publications.ListAllAsync(cancellationToken)).OrderByDescending(release => release.ReleaseNumber).ToArray();
        var votes = await BackfillVotesAsync(keptReleases, cancellationToken);
        var photos = await BackfillPhotoPathsAsync(keptReleases, cancellationToken);
        var congresses = await BackfillCongressesAsync(cancellationToken);
        return new(votes.Examined, votes.Written, votes.WithoutFile, votes.OtherParties, photos, congresses);
    }

    private async Task<(int Examined, int Written, int WithoutFile, int OtherParties)> BackfillVotesAsync(PublicationReleaseSummary[] keptReleases,
        CancellationToken cancellationToken)
    {
        var gaps = (await publications.ReadVoteColumnGapsAsync(cancellationToken)).Distinct().ToArray();
        int written = 0, withoutFile = 0, otherParties = 0;
        foreach (var batch in gaps.Chunk(BatchSize))
        {
            var columns = new List<VoteStoredColumns>();
            foreach (var gap in batch)
            {
                if (await ReadColumnsAsync(keptReleases, gap, cancellationToken) is { } found) columns.Add(found);
                else withoutFile++;
            }
            var conflicts = await publications.WriteVoteColumnsAsync(columns, cancellationToken);
            otherParties += conflicts.Count;
            written += columns.Count - conflicts.Count;
        }
        return (gaps.Length, written, withoutFile, otherParties);
    }

    private async Task<VoteStoredColumns?> ReadColumnsAsync(PublicationReleaseSummary[] keptReleases, PublishedRevisionBinding gap,
        CancellationToken cancellationToken)
    {
        foreach (var release in keptReleases)
        {
            if (await ReadVoteAsync(release, gap, cancellationToken) is { } vote) return VoteStoredColumns.From(vote);
        }
        return null;
    }

    private async Task<PublishedVote?> ReadVoteAsync(PublicationReleaseSummary release, PublishedRevisionBinding gap, CancellationToken cancellationToken)
    {
        try
        {
            var vote = ReleaseRecordFiles.Deserialize<PublishedVote>(await ReleaseRecordFiles.ReadAsync(releases, release.DirectoryName, gap.RecordId, cancellationToken));
            vote.Validate();
            return vote.RevisionNumber == gap.RevisionNumber ? vote : null;
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or JsonException)
        {
            return null;
        }
    }

    private async Task<int> BackfillPhotoPathsAsync(PublicationReleaseSummary[] keptReleases, CancellationToken cancellationToken)
    {
        var filled = 0;
        foreach (var release in keptReleases)
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
}
