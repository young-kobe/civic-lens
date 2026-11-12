using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Proves in read-only transactions that the stored party tally groups of each vote revision equal the groups that <see cref="VoteTally.TalliesOf"/> rebuilds from the
/// member list rows of the exact revision. It reports each missing group, extra group, and group with different counts, and never writes.
/// A null marker stays unresolved and a zero marker stays unavailable, because a member list never proves that a published file had a complete tally.
/// The cursor resumes after its last key. A fresh scan is needed to include revisions added before that key.
/// </summary>
public sealed class VerifyVotePartyTallies(IPublicationStore publications)
{
    private const int BatchSize = 25;
    private const int MaximumReportedFailures = 50;

    public async Task<VotePartyTallyVerificationResult> ExecuteAsync(ReviewActor actor, int limit, VotePartyTallyVerificationCursor? after,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        int examined = 0, verified = 0, unavailable = 0, unresolved = 0, failed = 0;
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
                var found = Check(evidence);
                if (found.Count > 0) failed++;
                else if (evidence.ExpectedGroupCount is null) unresolved++;
                else if (evidence.ExpectedGroupCount == 0) unavailable++;
                else verified++;
                failures.AddRange(found.Take(MaximumReportedFailures - failures.Count));
            }
            if (!page.HasMore) break;
            if (examined >= limit) next = cursor?.Encode();
        }
        return new(examined, verified, unavailable, unresolved, failed, failures, next);
    }

    private static List<VotePartyTallyFailure> Check(VotePartyTallyEvidence evidence)
    {
        var failures = new List<VotePartyTallyFailure>();
        void Fail(string reason, string detail) => failures.Add(new(evidence.RecordId, evidence.RevisionNumber, reason, detail));
        if (evidence.ExpectedGroupCount is null)
        {
            if (evidence.StoredGroups.Count > 0) Fail(VotePartyTallyFailure.RowsWithoutMarker, "Group rows exist while the expected group count is unknown.");
            return failures;
        }
        if (evidence.ExpectedGroupCount == 0)
        {
            foreach (var stored in evidence.StoredGroups) Fail(VotePartyTallyFailure.ExtraGroup, $"{stored.Party}: a row exists while the count is marked unavailable.");
            return failures;
        }
        if (evidence.ExpectedGroupCount != evidence.StoredGroups.Count)
            Fail(VotePartyTallyFailure.MarkerWithoutRows, $"The expected group count is {evidence.ExpectedGroupCount} and {evidence.StoredGroups.Count} rows are stored.");
        if (evidence.Members.Any(member => member.Party is null))
        {
            Fail(VotePartyTallyFailure.MemberPartyMissing, "A member list row has no party, so the groups cannot be rebuilt.");
            return failures;
        }
        CompareGroups(evidence, Fail);
        return failures;
    }

    private static void CompareGroups(VotePartyTallyEvidence evidence, Action<string, string> fail)
    {
        var rebuilt = VoteTally.TalliesOf(evidence.Members.Select(member => (member.Party!, member.CastText))).ToDictionary(tally => tally.Party, StringComparer.Ordinal);
        var stored = evidence.StoredGroups.ToDictionary(tally => tally.Party, StringComparer.Ordinal);
        foreach (var (party, expected) in rebuilt)
        {
            if (!stored.TryGetValue(party, out var actual)) fail(VotePartyTallyFailure.MissingGroup, $"{party}: no row is stored.");
            else if (actual != expected) fail(VotePartyTallyFailure.DifferentCounts, $"{party}: stored {Describe(actual)}, rebuilt {Describe(expected)}.");
        }
        foreach (var party in stored.Keys.Where(party => !rebuilt.ContainsKey(party)))
            fail(VotePartyTallyFailure.ExtraGroup, $"{party}: no member of the list has this party.");
    }

    private static string Describe(PublishedPartyTally tally) =>
        $"yea {tally.Yea}, nay {tally.Nay}, present {tally.Present}, not voting {tally.NotVoting}, other {tally.Other}";
}
