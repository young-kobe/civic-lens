using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Finds the immutable record file of one exact vote revision in the kept folders of its release range. The newest folder in the range wins.
/// A missing, invalid, or other-revision file falls through to the next older folder.
/// </summary>
internal static class KeptVoteFiles
{
    public static async Task<PublishedVote?> ReadAsync(IReleaseDirectory releases, IEnumerable<PublicationReleaseSummary> newestFirst, string recordId,
        int revisionNumber, KeptReleaseRange range, CancellationToken cancellationToken)
    {
        foreach (var release in newestFirst.Where(release => range.Covers(release.ReleaseNumber)))
        {
            if (await ReadAsync(releases, release, recordId, revisionNumber, cancellationToken) is { } vote) return vote;
        }
        return null;
    }

    private static async Task<PublishedVote?> ReadAsync(IReleaseDirectory releases, PublicationReleaseSummary release, string recordId, int revisionNumber,
        CancellationToken cancellationToken)
    {
        try
        {
            var vote = ReleaseRecordFiles.Deserialize<PublishedVote>(await ReleaseRecordFiles.ReadAsync(releases, release.DirectoryName, recordId, cancellationToken));
            vote.Validate();
            return vote.RecordId == recordId && vote.RevisionNumber == revisionNumber ? vote : null;
        }
        catch (Exception exception) when (exception is FileNotFoundException or InvalidDataException or JsonException)
        {
            return null;
        }
    }
}
