using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Proves that the stored party tally groups of each vote revision equal the party tallies of its kept vote file, the same immutable file that
/// <see cref="BackfillStoredColumns"/> converts and that publication wrote from the full roll. A member list holds tracked members only, so it never proves a group.
/// Stored rows are read in read-only transactions, and the command never writes. A null marker stays unresolved and a zero marker stays unavailable.
/// A positive marker whose file no kept folder holds stays unproven, counted apart for an open and a closed release range. The cursor resumes after its last key. A fresh scan is needed to include revisions added before that key.
/// </summary>
public sealed class VerifyVotePartyTallies(IPublicationStore publications, IReleaseDirectory releases)
{
    private const int BatchSize = 25;
    private const int MaximumReportedFailures = 50;

    public async Task<VotePartyTallyVerificationResult> ExecuteAsync(ReviewActor actor, int limit, VotePartyTallyVerificationCursor? after,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        var newestFirst = (await publications.ListAllAsync(cancellationToken)).OrderByDescending(release => release.ReleaseNumber).ToArray();
        int examined = 0, verified = 0, unavailable = 0, unresolved = 0, openWithoutKeptFile = 0, closedWithoutKeptFile = 0, failed = 0;
        var failures = new List<VotePartyTallyFailure>();
        var cursor = after;
        string? next = null;
        while (examined < limit)
        {
            var page = await publications.ReadVotePartyTallyEvidenceAsync(cursor, Math.Min(BatchSize, limit - examined), cancellationToken);
            foreach (var evidence in page.Revisions)
            {
                examined++;
                cursor = new(evidence.RecordId, evidence.RevisionNumber);
                var (found, outcome) = await CheckAsync(evidence, newestFirst, cancellationToken);
                if (found.Count > 0) failed++;
                else if (outcome == Outcome.Unresolved) unresolved++;
                else if (outcome == Outcome.Unavailable) unavailable++;
                else if (outcome == Outcome.OpenWithoutKeptFile) openWithoutKeptFile++;
                else if (outcome == Outcome.ClosedWithoutKeptFile) closedWithoutKeptFile++;
                else verified++;
                failures.AddRange(found.Take(MaximumReportedFailures - failures.Count));
            }
            if (!page.HasMore) break;
            if (examined >= limit) next = cursor?.Encode();
        }
        return new(examined, verified, unavailable, unresolved, openWithoutKeptFile, closedWithoutKeptFile, failed, failures, next);
    }

    private async Task<(List<VotePartyTallyFailure> Failures, Outcome Outcome)> CheckAsync(VotePartyTallyEvidence evidence, PublicationReleaseSummary[] newestFirst,
        CancellationToken cancellationToken)
    {
        var failures = new List<VotePartyTallyFailure>();
        void Fail(string reason, string detail) => failures.Add(new(evidence.RecordId, evidence.RevisionNumber, reason, detail));
        if (evidence.ExpectedGroupCount is not { } marker)
        {
            if (evidence.StoredGroups.Count > 0) Fail(VotePartyTallyFailure.RowsWithoutMarker, "Group rows exist while the expected group count is unknown.");
            return (failures, Outcome.Unresolved);
        }
        if (marker == 0)
            foreach (var stored in evidence.StoredGroups) Fail(VotePartyTallyFailure.ExtraGroup, $"{stored.Party}: a row exists while the count is marked unavailable.");
        else if (marker != evidence.StoredGroups.Count)
            Fail(VotePartyTallyFailure.MarkerWithoutRows, $"The expected group count is {marker} and {evidence.StoredGroups.Count} rows are stored.");

        var file = evidence.Releases is { } range
            ? await KeptVoteFiles.ReadAsync(releases, newestFirst, evidence.RecordId, evidence.RevisionNumber, range, cancellationToken)
            : null;
        if (file is null) return (failures, marker == 0 ? Outcome.Unavailable : evidence.Releases?.IsOpen == true ? Outcome.OpenWithoutKeptFile : Outcome.ClosedWithoutKeptFile);
        var expected = VoteStoredColumns.From(file).PartyTallies is { IsDefault: false } tallies ? tallies.ToArray() : [];
        if (expected.Length != marker)
            Fail(VotePartyTallyFailure.MarkerDiffersFromFile, $"The expected group count is {marker} and the kept vote file holds {expected.Length} groups.");
        if (marker > 0) CompareGroups(expected, evidence.StoredGroups, Fail);
        return (failures, marker == 0 ? Outcome.Unavailable : Outcome.Verified);
    }

    private static void CompareGroups(PublishedPartyTally[] expected, IReadOnlyList<PublishedPartyTally> storedGroups, Action<string, string> fail)
    {
        var fromFile = expected.ToDictionary(tally => tally.Party, StringComparer.Ordinal);
        var stored = storedGroups.ToDictionary(tally => tally.Party, StringComparer.Ordinal);
        foreach (var (party, tally) in fromFile)
        {
            if (!stored.TryGetValue(party, out var actual)) fail(VotePartyTallyFailure.MissingGroup, $"{party}: no row is stored.");
            else if (actual != tally) fail(VotePartyTallyFailure.DifferentCounts, $"{party}: stored {Describe(actual)}, file {Describe(tally)}.");
        }
        foreach (var party in stored.Keys.Where(party => !fromFile.ContainsKey(party)))
            fail(VotePartyTallyFailure.ExtraGroup, $"{party}: the kept vote file has no group for this party.");
    }

    private static string Describe(PublishedPartyTally tally) =>
        $"yea {tally.Yea}, nay {tally.Nay}, present {tally.Present}, not voting {tally.NotVoting}, other {tally.Other}";

    private enum Outcome { Verified, Unavailable, Unresolved, OpenWithoutKeptFile, ClosedWithoutKeptFile }
}
