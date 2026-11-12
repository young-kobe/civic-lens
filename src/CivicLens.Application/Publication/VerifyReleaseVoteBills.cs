using CivicLens.Application.Review;
using CivicLens.Application.Votes;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Proves that the stored vote links of each kept release equal the links of the old rule over its manifest. The command never writes and takes no lock.
/// A release fails when its manifest cannot be read, when a link is missing, when a row has no expected link, or when a row names another bill action.
/// A release with no manifest folder is retired. The run goes on after a failed release.
/// </summary>
public sealed class VerifyReleaseVoteBills(IPublicationStore publications, IReleaseDirectory releases, IVoteReviewStore votes)
{
    private const string ManifestInvalid = "manifestInvalid";
    private const string LinkMissing = "linkMissing";
    private const string LinkExtra = "linkExtra";
    private const string LinkChanged = "linkChanged";

    public async Task<ReleaseVoteBillsVerification> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var passed = 0;
        var retired = 0;
        var failures = new List<FailedRelease>();
        foreach (var release in await publications.ListAllAsync(cancellationToken))
        {
            var (isRetired, failure) = await CheckAsync(release, cancellationToken);
            if (isRetired) retired++;
            else if (failure is null) passed++;
            else failures.Add(failure);
        }
        var byReason = failures.GroupBy(failure => failure.Reason).ToDictionary(group => group.Key, group => group.Count());
        return new(passed, failures.Count, retired, byReason, failures);
    }

    private async Task<(bool Retired, FailedRelease? Failure)> CheckAsync(PublicationReleaseSummary release, CancellationToken cancellationToken)
    {
        PublicationRelease manifest;
        try
        {
            manifest = await PreviousRelease.LoadManifestAsync(releases, release, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return (true, null);
        }
        catch (InvalidDataException)
        {
            return (false, Fail(release, ManifestInvalid, null));
        }
        var expected = (await ReleaseVoteBillLinks.ExpectedAsync(votes, manifest, cancellationToken)).ToDictionary(link => link.VoteRecordId, StringComparer.Ordinal);
        var stored = (await publications.ReadVoteBillsAsync(release.ReleaseNumber, cancellationToken)).ToDictionary(link => link.VoteRecordId, StringComparer.Ordinal);
        return (false, FindBreak(release, expected, stored));
    }

    private static FailedRelease? FindBreak(PublicationReleaseSummary release, Dictionary<string, ReleaseVoteBill> expected, Dictionary<string, ReleaseVoteBill> stored)
    {
        if (expected.Keys.Order(StringComparer.Ordinal).FirstOrDefault(voteRecordId => !stored.ContainsKey(voteRecordId)) is { } missing)
            return Fail(release, LinkMissing, missing);
        if (stored.Keys.Order(StringComparer.Ordinal).FirstOrDefault(voteRecordId => !expected.ContainsKey(voteRecordId)) is { } extra)
            return Fail(release, LinkExtra, extra);
        var changed = expected.Keys.Order(StringComparer.Ordinal).FirstOrDefault(voteRecordId => expected[voteRecordId] != stored[voteRecordId]);
        return changed is null ? null : Fail(release, LinkChanged, changed);
    }

    private static FailedRelease Fail(PublicationReleaseSummary release, string reason, string? voteRecordId) =>
        new(release.ReleaseNumber, release.DirectoryName, reason, voteRecordId is null ? null : $"vote:{voteRecordId}");
}
