using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Infrastructure.Collection.BillStream;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore
{
    private static readonly DiscoveryAdmissionResult NoAdmission = new([], 0, 0);

    public Task<DiscoveryAdmissionResult> AdmitDetailAsync(ConfiguredCollectionSource source, string detailJobId,
        CongressBill bill, BillGateOutcome gate, string issueRevisionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailJobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(issueRevisionId);
        return TransactionAsync(async (db, now) =>
        {
            if (await IsGatedAsync(db, source.SourceId, bill, cancellationToken)) return NoAdmission with { DuplicateCount = 1 };
            db.Add(NewGateRow(source.SourceId, detailJobId, bill, gate, issueRevisionId, now));
            return await AdmitFollowOnsAsync(db, source, detailJobId, DetailFollowOns(source, bill, gate, now), now,
                cancellationToken);
        }, cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitCosponsorPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillCosponsorsPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageJobId);
        return TransactionAsync(async (db, now) =>
        {
            var gate = await ReadGateOfPageJobAsync(db, pageJobId, cancellationToken);
            var jobs = new List<BillStreamJob>();
            if (page.Next is not null)
                jobs.Add(source.CreateNextPageJob(ApiResource.BillCosponsors, pageJobId, page.Next, Today(now)));
            if (gate.Decision == BillGateDecision.Pending && SettleAtCosponsorPage(gate, page, source, now))
                jobs.Add(ActionsFirstPage(source, gate, now));
            return await AdmitFollowOnsAsync(db, source, gate.DetailJobId, jobs, now, cancellationToken);
        }, cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitActionsPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillActionsPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageJobId);
        return TransactionAsync(async (db, now) =>
        {
            var detailJobId = await ReadDetailJobIdOfPageJobAsync(db, pageJobId, cancellationToken);
            BillStreamJob[] jobs = page.Next is null
                ? []
                : [source.CreateNextPageJob(ApiResource.BillActions, pageJobId, page.Next, Today(now))];
            return await AdmitFollowOnsAsync(db, source, detailJobId, jobs, now, cancellationToken);
        }, cancellationToken);
    }

    private static async Task<DiscoveryAdmissionResult> AdmitFollowOnsAsync(CollectionAttemptDbContext db,
        ConfiguredCollectionSource source, string detailJobId, IReadOnlyList<BillStreamJob> jobs, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (jobs.Count == 0) return NoAdmission;
        var batch = source.CreateBillStreamBatch(jobs, Today(now));
        var (result, admitted) = await AdmitBillStreamJobsAsync(db, batch, int.MaxValue, now, cancellationToken);
        if (result.DeferredCount > 0)
            throw new InvalidDataException($"Source '{source.SourceId}' admission policy cannot hold the follow-on jobs of a bill.");
        foreach (var item in admitted)
            db.Add(new BillFollowOnJobRow { JobId = item.JobId, DetailJobId = detailJobId });
        return result;
    }

    private static List<BillStreamJob> DetailFollowOns(ConfiguredCollectionSource source, CongressBill bill,
        BillGateOutcome gate, DateTimeOffset now)
    {
        var jobs = new List<BillStreamJob>();
        if (bill.CosponsorCount > 0)
            jobs.Add(source.CreateFirstPageJob(ApiResource.BillCosponsors, IdentityOf(bill), bill.UpdateDate, Today(now)));
        if (gate.IsPassed)
            jobs.Add(source.CreateFirstPageJob(ApiResource.BillActions, IdentityOf(bill), bill.UpdateDate, Today(now)));
        return jobs;
    }

    private static bool SettleAtCosponsorPage(BillGateRow gate, CongressBillCosponsorsPage page,
        ConfiguredCollectionSource source, DateTimeOffset now)
    {
        var covered = source.CoveredBioguideIdsOn(BillGate.DayOf(UpdateDateOf(gate)));
        var outcome = BillGate.AtCosponsorPage(gate.PolicyArea, page, covered);
        if (outcome.IsPending) return false;
        gate.Decision = outcome.Decision;
        gate.Reason = outcome.Reason;
        gate.DecidedAtUtcTicks = Ticks(now);
        return outcome.IsPassed;
    }

    private static BillStreamJob ActionsFirstPage(ConfiguredCollectionSource source, BillGateRow gate, DateTimeOffset now) =>
        source.CreateFirstPageJob(ApiResource.BillActions, IdentityOf(gate), UpdateDateOf(gate), Today(now));

    private static Task<bool> IsGatedAsync(CollectionAttemptDbContext db, string sourceId, CongressBill bill,
        CancellationToken cancellationToken)
    {
        var updateTicks = bill.UpdateDate.UtcTicks;
        return db.Set<BillGateRow>().AnyAsync(row => row.SourceId == sourceId && row.Congress == bill.Congress &&
            row.BillType == bill.Type && row.BillNumber == bill.Number && row.UpdateDateUtcTicks == updateTicks,
            cancellationToken);
    }

    private static BillGateRow NewGateRow(string sourceId, string detailJobId, CongressBill bill, BillGateOutcome gate,
        string issueRevisionId, DateTimeOffset now) => new()
        {
            DetailJobId = detailJobId,
            SourceId = sourceId,
            Congress = bill.Congress,
            BillType = bill.Type,
            BillNumber = bill.Number,
            UpdateDateUtcTicks = bill.UpdateDate.UtcTicks,
            PolicyArea = bill.PolicyArea,
            IssueRevisionId = issueRevisionId,
            Decision = gate.Decision,
            Reason = gate.Reason,
            CreatedAtUtcTicks = Ticks(now),
            DecidedAtUtcTicks = gate.IsPending ? null : Ticks(now)
        };

    private static async Task<BillGateRow> ReadGateOfPageJobAsync(CollectionAttemptDbContext db, string pageJobId,
        CancellationToken cancellationToken)
    {
        var detailJobId = await ReadDetailJobIdOfPageJobAsync(db, pageJobId, cancellationToken);
        return await db.Set<BillGateRow>().SingleAsync(row => row.DetailJobId == detailJobId, cancellationToken);
    }

    private static async Task<string> ReadDetailJobIdOfPageJobAsync(CollectionAttemptDbContext db, string pageJobId,
        CancellationToken cancellationToken) =>
        await db.Set<BillFollowOnJobRow>().Where(row => row.JobId == pageJobId).Select(row => row.DetailJobId)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new InvalidDataException("Bill page job has no link to a detail job.");

    private static CongressBillListItem IdentityOf(CongressBill bill) =>
        new(bill.Congress, bill.Type, bill.Number, BillGate.DayOf(bill.UpdateDate));

    private static CongressBillListItem IdentityOf(BillGateRow gate) =>
        new(gate.Congress, gate.BillType, gate.BillNumber, BillGate.DayOf(UpdateDateOf(gate)));

    private static DateTimeOffset UpdateDateOf(BillGateRow gate) => new(gate.UpdateDateUtcTicks, TimeSpan.Zero);

    private static DateOnly Today(DateTimeOffset now) => DateOnly.FromDateTime(now.UtcDateTime);
}
