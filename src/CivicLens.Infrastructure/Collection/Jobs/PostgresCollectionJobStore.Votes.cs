using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Core.Votes;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Collection.Votes;
using CivicLens.Infrastructure.Statements;
using CivicLens.Infrastructure.Votes;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore : IVoteAdmissionStore
{
    public Task<DiscoveryAdmissionResult> AdmitListPageAsync(ConfiguredCollectionSource source, string listJobId,
        HouseVoteListPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(listJobId);
        return TransactionAsync(async (db, now) =>
        {
            var batch = source.CreateHouseVoteListBatch(listJobId, page, Today(now));
            var (result, _) = await AdmitApiJobsAsync(db, batch, ApiJobSaveBatchSize, now, cancellationToken);
            if (result.DeferredCount > 0)
                throw new InvalidDataException($"Source '{source.SourceId}' admission policy cannot hold a full House vote list page.");
            return result;
        }, cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
        HouseVote vote, string extractionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(vote);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailJobId);
        return TransactionAsync(async (db, now) =>
        {
            if (await FindVoteAsync(db, source.SourceId, Chamber.House, vote.Roll.Session, vote.Roll.Number, vote.UpdateDate, cancellationToken) is not null)
                return NoAdmission with { DuplicateCount = 1 };
            await EnsureExtractionIsOfJobAsync(db, detailJobId, extractionId, cancellationToken);
            db.Add(NewVoteRow(source.SourceId, detailJobId, Chamber.House, vote.Roll.Session, vote.Roll.Number, vote.UpdateDate,
                vote.BillType, vote.BillNumber, extractionId));
            var membersJob = source.CreateHouseVoteMembersJob(vote, Today(now));
            var batch = source.CreateApiBatch([membersJob], Today(now));
            var (result, _) = await AdmitApiJobsAsync(db, batch, int.MaxValue, now, cancellationToken);
            if (result.DeferredCount > 0)
                throw new InvalidDataException($"Source '{source.SourceId}' admission policy cannot hold the members job of a House vote.");
            return result;
        }, cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitMembersAsync(ConfiguredCollectionSource source, string membersJobId,
        HouseVote vote, string extractionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(vote);
        ArgumentException.ThrowIfNullOrWhiteSpace(membersJobId);
        return TransactionAsync(async (db, now) =>
        {
            var row = await FindVoteAsync(db, source.SourceId, Chamber.House, vote.Roll.Session, vote.Roll.Number, vote.UpdateDate,
                cancellationToken)
                ?? throw new InvalidDataException("The members belong to a House vote version that has no recorded detail.");
            if (await db.Set<VoteCandidateRow>().AnyAsync(candidate => candidate.DetailJobId == row.DetailJobId, cancellationToken))
                return NoAdmission with { DuplicateCount = 1 };
            await EnsureExtractionIsOfJobAsync(db, membersJobId, extractionId, cancellationToken);
            var candidate = NewVoteCandidate(source, row, membersJobId, extractionId, now);
            db.Add(candidate);
            await QueueCandidateAsync(db, candidate, now, cancellationToken);
            return NoAdmission;
        }, cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitSenateMenuAsync(ConfiguredCollectionSource source, string menuJobId,
        SenateVoteMenu menu, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentException.ThrowIfNullOrWhiteSpace(menuJobId);
        return TransactionAsync(async (db, now) =>
        {
            var batch = source.CreateSenateMenuBatch(menuJobId, menu, Today(now));
            var (result, _) = await AdmitApiJobsAsync(db, batch, ApiJobSaveBatchSize, now, cancellationToken);
            if (result.DeferredCount > 0)
                throw new InvalidDataException($"Source '{source.SourceId}' admission policy cannot hold a full Senate vote menu.");
            return result;
        }, cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitSenateVoteAsync(ConfiguredCollectionSource source, string voteJobId,
        SenateVote vote, string extractionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(vote);
        ArgumentException.ThrowIfNullOrWhiteSpace(voteJobId);
        return TransactionAsync(async (db, now) =>
        {
            var session = vote.Roll.Session;
            if (await FindVoteAsync(db, source.SourceId, Chamber.Senate, session, vote.Roll.Number, vote.ModifyDate, cancellationToken) is not null)
                return NoAdmission with { DuplicateCount = 1 };
            await EnsureExtractionIsOfJobAsync(db, voteJobId, extractionId, cancellationToken);
            var row = NewVoteRow(source.SourceId, voteJobId, Chamber.Senate, session, vote.Roll.Number, vote.ModifyDate,
                vote.BillType, vote.BillNumber, extractionId);
            db.Add(row);
            var candidate = NewVoteCandidate(source, row, voteJobId, extractionId, now);
            db.Add(candidate);
            await QueueCandidateAsync(db, candidate, now, cancellationToken);
            return NoAdmission;
        }, cancellationToken);
    }

    private static async Task QueueCandidateAsync(CollectionAttemptDbContext db, VoteCandidateRow candidate, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (await IsWaitingForBillDetailAsync(db, candidate, cancellationToken)) return;
        VoteDraftQueueRows.Add(db, candidate.CandidateId, Ticks(now), await PriorityOfVoteAsync(db, candidate, cancellationToken));
    }

    private static async Task<bool> IsWaitingForBillDetailAsync(CollectionAttemptDbContext db, VoteCandidateRow candidate,
        CancellationToken cancellationToken) =>
        candidate.BillType is not null && !await BillDetailRows
            .PagesNewestFirst(db, candidate.Congress, candidate.BillType, candidate.BillNumber!.Value).AnyAsync(cancellationToken);

    private static async Task<int> PriorityOfVoteAsync(CollectionAttemptDbContext db, VoteCandidateRow candidate,
        CancellationToken cancellationToken)
    {
        var hasTrackedIssue = candidate.BillType is not null && await db.Set<BillGateRow>().AsNoTracking()
            .Where(gate => gate.Congress == candidate.Congress && gate.BillType == candidate.BillType && gate.BillNumber == candidate.BillNumber)
            .OrderByDescending(gate => gate.UpdateDateUtcTicks).Select(gate => gate.HasTrackedIssue).FirstOrDefaultAsync(cancellationToken);
        return await StatementBillMentions.PriorityOfBillAsync(db, candidate.Congress, candidate.BillType, candidate.BillNumber, hasTrackedIssue,
            cancellationToken);
    }

    private static async Task QueueVotesWaitingForBillAsync(CollectionAttemptDbContext db, CongressBill bill, bool hasTrackedIssue,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var waiting = await VoteDraftQueueRows.WaitingForBill(db).Where(candidate => candidate.Congress == bill.Congress &&
            candidate.BillType == bill.Type && candidate.BillNumber == bill.Number)
            .Select(candidate => candidate.CandidateId).ToListAsync(cancellationToken);
        if (waiting.Count == 0) return;
        var priority = await StatementBillMentions.PriorityOfBillAsync(db, bill.Congress, bill.Type, bill.Number, hasTrackedIssue, cancellationToken);
        foreach (var candidateId in waiting) VoteDraftQueueRows.Add(db, candidateId, Ticks(now), priority);
    }

    private static string HashVoteCandidate(Chamber chamber, string sourceId, int congress, int sessionNumber, int rollCallNumber,
        long updateDateUtcTicks) =>
        HashIdentity($"{IdentityPrefix(chamber)}:{sourceId.Length}:{sourceId}{congress}:{sessionNumber}:{rollCallNumber}:{updateDateUtcTicks}");

    internal static string HashVoteRecord(Chamber chamber, string sourceId, int congress, int sessionNumber, int rollCallNumber) =>
        HashIdentity($"{IdentityPrefix(chamber)}-record:{sourceId.Length}:{sourceId}{congress}:{sessionNumber}:{rollCallNumber}");

    private static string IdentityPrefix(Chamber chamber) => $"{chamber.ToString().ToLowerInvariant()}-vote";

    private static Task<VoteVersionRow?> FindVoteAsync(CollectionAttemptDbContext db, string sourceId, Chamber chamber,
        VoteSession session, int rollCallNumber, DateTimeOffset updateDate, CancellationToken cancellationToken)
    {
        var updateTicks = updateDate.UtcTicks;
        return db.Set<VoteVersionRow>().SingleOrDefaultAsync(row => row.SourceId == sourceId && row.Chamber == chamber &&
            row.Congress == session.Congress &&
            row.SessionNumber == session.Number && row.RollCallNumber == rollCallNumber && row.UpdateDateUtcTicks == updateTicks,
            cancellationToken);
    }

    private static VoteVersionRow NewVoteRow(string sourceId, string detailJobId, Chamber chamber, VoteSession session,
        int rollCallNumber, DateTimeOffset updateDate, string? billType, int? billNumber, string extractionId) => new()
        {
            DetailJobId = detailJobId,
            SourceId = sourceId,
            Chamber = chamber,
            Congress = session.Congress,
            SessionNumber = session.Number,
            RollCallNumber = rollCallNumber,
            UpdateDateUtcTicks = updateDate.UtcTicks,
            BillType = billType,
            BillNumber = billNumber,
            ExtractionId = extractionId
        };

    private static VoteCandidateRow NewVoteCandidate(ConfiguredCollectionSource source, VoteVersionRow vote,
        string membersJobId, string membersExtractionId, DateTimeOffset now) => new()
        {
            CandidateId = HashVoteCandidate(vote.Chamber, vote.SourceId, vote.Congress, vote.SessionNumber, vote.RollCallNumber,
                vote.UpdateDateUtcTicks),
            SourceId = vote.SourceId,
            Chamber = vote.Chamber,
            Congress = vote.Congress,
            SessionNumber = vote.SessionNumber,
            RollCallNumber = vote.RollCallNumber,
            UpdateDateUtcTicks = vote.UpdateDateUtcTicks,
            BillType = vote.BillType,
            BillNumber = vote.BillNumber,
            DetailJobId = vote.DetailJobId,
            MembersJobId = membersJobId,
            DetailExtractionId = vote.ExtractionId,
            MembersExtractionId = membersExtractionId,
            ConfigurationRevisionId = source.Revision.Id,
            CreatedAtUtcTicks = Ticks(now)
        };
}
