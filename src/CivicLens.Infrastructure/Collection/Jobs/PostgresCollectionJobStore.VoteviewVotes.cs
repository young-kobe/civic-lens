using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.VoteviewVotes;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Collection.Votes;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore
{
    public Task<DiscoveryAdmissionResult> AdmitVoteviewFollowOnsAsync(ConfiguredCollectionSource source, ApiResource finished,
        string jobId, int congress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        return TransactionAsync(async (db, now) =>
        {
            var batch = source.CreateVoteviewFollowOnBatch(finished, jobId, congress, Today(now));
            var (result, _) = await AdmitApiJobsAsync(db, batch, int.MaxValue, now, cancellationToken);
            if (result.DeferredCount > 0)
                throw new InvalidDataException($"Source '{source.SourceId}' admission policy cannot hold the follow-on jobs of a Voteview file.");
            return result;
        }, cancellationToken);
    }

    public async Task<bool> HasVoteviewCandidateOfBodiesAsync(ConfiguredCollectionSource source, int congress, VoteviewBodies bodies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bodies);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var newest = await db.Set<VoteCandidateRow>().AsNoTracking()
            .Where(candidate => candidate.SourceId == source.SourceId && candidate.Chamber == Chamber.Senate && candidate.Congress == congress &&
                candidate.RosterJobId != null)
            .OrderByDescending(candidate => candidate.CreatedAtUtcTicks).FirstOrDefaultAsync(cancellationToken);
        return newest is not null &&
            await db.Set<DocumentExtractionRow>().AsNoTracking().AnyAsync(extraction => extraction.ExtractionId == newest.DetailExtractionId &&
                extraction.ExtractionVersion == bodies.ExtractionVersion, cancellationToken) &&
            await CaptureSha256OfJobAsync(db, newest.DetailJobId, cancellationToken) == bodies.RollCallsSha256 &&
            await CaptureSha256OfJobAsync(db, newest.RosterJobId!, cancellationToken) == bodies.MembersSha256 &&
            await CaptureSha256OfJobAsync(db, newest.MembersJobId, cancellationToken) == bodies.VotesSha256;
    }

    public async Task<IReadOnlyList<VoteviewRollExtraction>> SelectChangedVoteviewRollsAsync(ConfiguredCollectionSource source,
        int congress, IReadOnlyList<VoteviewRollExtraction> rolls, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rolls);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await ChangedVoteviewRollsAsync(db, source.SourceId, congress, rolls, cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitVoteviewVotesAsync(ConfiguredCollectionSource source, VoteviewChain chain,
        DateTimeOffset votesCapturedAt, IReadOnlyList<VoteviewRollExtraction> rolls, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(rolls);
        return TransactionAsync(async (db, now) =>
        {
            var changed = await ChangedVoteviewRollsAsync(db, source.SourceId, chain.Congress, rolls, cancellationToken);
            if (changed.Count == 0) return NoAdmission with { DuplicateCount = rolls.Count };
            await EnsureExtractionsAreOfJobAsync(db, chain.VotesJobId, changed, cancellationToken);
            var queueInputsByBill = new Dictionary<(string? Type, int? Number), QueueInputs>();
            foreach (var roll in changed)
            {
                var call = roll.RollCall;
                var row = NewVoteRow(source.SourceId, chain.RollCallsJobId, Chamber.Senate, call.Roll.Session, call.Roll.Number, votesCapturedAt,
                    call.BillType, call.BillNumber, roll.Extraction.ExtractionId);
                db.Add(row);
                var candidate = NewVoteCandidate(source, row, chain.VotesJobId, roll.Extraction.ExtractionId, now);
                candidate.RosterJobId = chain.MembersJobId;
                db.Add(candidate);
                var bill = (candidate.BillType, candidate.BillNumber);
                if (!queueInputsByBill.TryGetValue(bill, out var inputs))
                    queueInputsByBill[bill] = inputs = await ReadQueueInputsAsync(db, candidate, cancellationToken);
                QueueCandidate(db, candidate, now, inputs);
            }
            return NoAdmission with { DuplicateCount = rolls.Count - changed.Count };
        }, cancellationToken);
    }

    private static async Task EnsureExtractionsAreOfJobAsync(CollectionAttemptDbContext db, string jobId,
        IReadOnlyList<VoteviewRollExtraction> rolls, CancellationToken cancellationToken)
    {
        if (rolls.Select(roll => roll.Extraction.SourceAttempt.AttemptId).Distinct(StringComparer.Ordinal).Count() != 1)
            throw new InvalidDataException("The roll calls of one Voteview votes job must come from one capture.");
        await EnsureExtractionIsOfJobAsync(db, jobId, rolls[0].Extraction.ExtractionId, cancellationToken);
    }

    private static async Task<IReadOnlyList<VoteviewRollExtraction>> ChangedVoteviewRollsAsync(CollectionAttemptDbContext db, string sourceId,
        int congress, IReadOnlyList<VoteviewRollExtraction> rolls, CancellationToken cancellationToken)
    {
        var versions = await (from version in db.Set<VoteVersionRow>().AsNoTracking()
                              where version.SourceId == sourceId && version.Chamber == Chamber.Senate && version.Congress == congress
                              join extraction in db.Set<DocumentExtractionRow>().AsNoTracking() on version.ExtractionId equals extraction.ExtractionId
                              select new { version.SessionNumber, version.RollCallNumber, version.UpdateDateUtcTicks, extraction.TextSha256 })
            .ToListAsync(cancellationToken);
        var newestTextHash = versions.GroupBy(version => (version.SessionNumber, version.RollCallNumber))
            .ToDictionary(group => group.Key, group => group.MaxBy(version => version.UpdateDateUtcTicks)!.TextSha256);
        return [.. rolls.Where(roll => !newestTextHash.TryGetValue((roll.RollCall.Roll.Session.Number, roll.RollCall.Roll.Number), out var hash) ||
            hash != roll.Extraction.TextSha256)];
    }

    private static Task<string?> CaptureSha256OfJobAsync(CollectionAttemptDbContext db, string jobId, CancellationToken cancellationToken) =>
        (from jobAttempt in db.Set<JobAttemptRow>().AsNoTracking()
         where jobAttempt.JobId == jobId
         join attempt in db.Attempts.AsNoTracking() on jobAttempt.AttemptId equals attempt.AttemptId
         where attempt.CaptureSha256 != null
         orderby attempt.ObservedAtUtcTicks descending
         select attempt.CaptureSha256).FirstOrDefaultAsync(cancellationToken);
}
