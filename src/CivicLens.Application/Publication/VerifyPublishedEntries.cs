using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Proves that the tables hold what each kept manifest holds. The command rebuilds the manifest of every kept folder from the state, entry, first
/// publication and withdrawal rows, and compares the bytes with the file. It writes nothing and takes no lock.
/// The first check reads and writes the file with no change, so a serializer change never looks like a table defect.
/// A release that fails a check gets a reason, and the run goes on. A release that passes but has withdrawals only in the file is legacy.
/// Each manifest is read once, one at a time.
/// </summary>
public sealed class VerifyPublishedEntries(IPublicationStore publications, IReleaseDirectory releases)
{
    private const string ManifestInvalid = "manifestInvalid";
    private const string ManifestNotReproducible = "manifestNotReproducible";
    private const string BytesDiffer = "bytesDiffer";

    public async Task<PublishedEntriesVerification> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        int passed = 0, legacy = 0, retired = 0;
        var failures = new List<FailedRelease>();
        foreach (var release in await publications.ListAllAsync(cancellationToken))
        {
            var outcome = await CheckAsync(release, cancellationToken);
            switch (outcome.Result)
            {
                case Result.Retired: retired++; break;
                case Result.Legacy: legacy++; break;
                case Result.Passed: passed++; break;
                default: failures.Add(new(release.ReleaseNumber, release.DirectoryName, outcome.Reason!, outcome.Record?.ToString())); break;
            }
        }
        var byReason = failures.GroupBy(failure => failure.Reason).ToDictionary(group => group.Key, group => group.Count());
        return new(passed, legacy, failures.Count, retired, byReason, failures);
    }

    private async Task<Outcome> CheckAsync(PublicationReleaseSummary release, CancellationToken cancellationToken)
    {
        byte[] bytes;
        try
        {
            bytes = await releases.ReadFileAsync(release.DirectoryName, PublicationProtocol.ManifestPath, PublicationProtocol.MaximumManifestBytes,
                cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return new(Result.Retired);
        }
        catch (InvalidDataException)
        {
            return Outcome.Failure(ManifestInvalid);
        }
        return await CheckManifestAsync(release, bytes, cancellationToken);
    }

    private async Task<Outcome> CheckManifestAsync(PublicationReleaseSummary release, byte[] bytes, CancellationToken cancellationToken)
    {
        PublicationRelease file;
        try
        {
            file = PreviousRelease.ParseManifest(bytes);
        }
        catch (InvalidDataException)
        {
            return Outcome.Failure(ManifestInvalid);
        }
        if (file.ReleaseNumber != release.ReleaseNumber) return Outcome.Failure(ManifestInvalid);
        if (!ReleaseSplice.Serialize(file).AsSpan().SequenceEqual(bytes)) return Outcome.Failure(ManifestNotReproducible);
        var stored = await publications.ReadReleaseAsync(release.ReleaseNumber, cancellationToken);
        if (ReleaseMembership.FindBreak(file, stored) is var (reason, record)) return Outcome.Failure(reason, record);
        var rebuilt = ReleaseSplice.Rebuild(file, stored);
        if (!ReleaseSplice.Serialize(rebuilt).AsSpan().SequenceEqual(bytes))
            return Outcome.Failure(BytesDiffer, ReleaseSplice.FirstDifferentRecord(file, rebuilt));
        return new(ReleaseSplice.HoldsWithdrawalsOnlyInFile(file, stored) ? Result.Legacy : Result.Passed);
    }

    private enum Result { Passed, Legacy, Retired, Failed }

    private sealed record Outcome(Result Result, string? Reason = null, PublishedRecordReference? Record = null)
    {
        public static Outcome Failure(string reason, PublishedRecordReference? record = null) => new(Result.Failed, reason, record);
    }
}
