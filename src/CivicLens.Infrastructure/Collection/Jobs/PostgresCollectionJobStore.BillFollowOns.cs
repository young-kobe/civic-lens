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
        CongressBill bill, BillGateOutcome gate, string issueRevisionId, string extractionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bill);
        ArgumentException.ThrowIfNullOrWhiteSpace(detailJobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(issueRevisionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionId);
        return TransactionAsync(async (db, now) =>
        {
            if (await IsGatedAsync(db, source.SourceId, bill, cancellationToken)) return NoAdmission with { DuplicateCount = 1 };
            var row = NewGateRow(source.SourceId, detailJobId, bill, gate, issueRevisionId, now);
            db.Add(row);
            await RecordPageAsync(db, detailJobId, detailJobId, BillPageRole.Detail, extractionId, closesChain: true, cancellationToken);
            await QueueVotesWaitingForBillAsync(db, bill, gate.HasTrackedIssue, now, cancellationToken);
            return await AdmitFollowOnsAsync(db, source, detailJobId, DetailFollowOns(source, bill, gate, row, now), now,
                cancellationToken);
        }, cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitCosponsorPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillCosponsorsPage page, string extractionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageJobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionId);
        return TransactionAsync(async (db, now) =>
        {
            var gate = await ReadGateOfPageJobAsync(db, pageJobId, cancellationToken);
            await RecordPageAsync(db, gate.DetailJobId, pageJobId, BillPageRole.Cosponsors, extractionId, page.Next is null,
                cancellationToken);
            var jobs = new List<ApiJob>();
            if (page.Next is not null)
                jobs.Add(source.CreateNextPageJob(ApiResource.BillCosponsors, pageJobId, page.Next, BillGate.DayOf(UpdateDateOf(gate)),
                    Today(now)));
            if (gate.Decision == BillGateDecision.Pending && SettleAtCosponsorPage(gate, page, source, now))
                jobs.AddRange(PassedFirstPages(source, gate, now));
            var result = await AdmitFollowOnsAsync(db, source, gate.DetailJobId, jobs, now, cancellationToken);
            if (page.Next is null) await RecordActionCandidateAsync(db, source, gate, now, cancellationToken);
            return result;
        }, cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitActionsPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillActionsPage page, string extractionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return AdmitNextPageAsync(source, pageJobId, BillPageRole.Actions, ApiResource.BillActions, page.Next, extractionId,
            cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitSummariesPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillSummariesPage page, string extractionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return AdmitNextPageAsync(source, pageJobId, BillPageRole.Summaries, ApiResource.BillSummaries, page.Next, extractionId,
            cancellationToken);
    }

    public Task<DiscoveryAdmissionResult> AdmitSubjectsPageAsync(ConfiguredCollectionSource source, string pageJobId,
        CongressBillSubjectsPage page, string extractionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        return AdmitNextPageAsync(source, pageJobId, BillPageRole.Subjects, ApiResource.BillSubjects, page.Next, extractionId,
            cancellationToken);
    }

    private Task<DiscoveryAdmissionResult> AdmitNextPageAsync(ConfiguredCollectionSource source, string pageJobId,
        BillPageRole role, ApiResource resource, string? next, string extractionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageJobId);
        ArgumentException.ThrowIfNullOrWhiteSpace(extractionId);
        return TransactionAsync(async (db, now) =>
        {
            var gate = await ReadGateOfPageJobAsync(db, pageJobId, cancellationToken);
            await RecordPageAsync(db, gate.DetailJobId, pageJobId, role, extractionId, next is null, cancellationToken);
            ApiJob[] jobs = next is null
                ? []
                : [source.CreateNextPageJob(resource, pageJobId, next, BillGate.DayOf(UpdateDateOf(gate)), Today(now))];
            var result = await AdmitFollowOnsAsync(db, source, gate.DetailJobId, jobs, now, cancellationToken);
            if (next is null) await RecordActionCandidateAsync(db, source, gate, now, cancellationToken);
            return result;
        }, cancellationToken);
    }

    private static async Task<DiscoveryAdmissionResult> AdmitFollowOnsAsync(CollectionAttemptDbContext db,
        ConfiguredCollectionSource source, string detailJobId, IReadOnlyList<ApiJob> jobs, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (jobs.Count == 0) return NoAdmission;
        var batch = source.CreateApiBatch(jobs, Today(now));
        var (result, admitted) = await AdmitApiJobsAsync(db, batch, int.MaxValue, now, cancellationToken);
        if (result.DeferredCount > 0)
            throw new InvalidDataException($"Source '{source.SourceId}' admission policy cannot hold the follow-on jobs of a bill.");
        foreach (var item in admitted)
            db.Add(new BillFollowOnJobRow { JobId = item.JobId, DetailJobId = detailJobId });
        return result;
    }

    private static List<ApiJob> DetailFollowOns(ConfiguredCollectionSource source, CongressBill bill,
        BillGateOutcome gate, BillGateRow row, DateTimeOffset now)
    {
        var jobs = new List<ApiJob>();
        if (row.HasCosponsorChain)
            jobs.Add(source.CreateFirstPageJob(ApiResource.BillCosponsors, IdentityOf(bill), bill.UpdateDate, Today(now)));
        if (row.HasSubjectsChain)
            jobs.Add(source.CreateFirstPageJob(ApiResource.BillSubjects, IdentityOf(bill), bill.UpdateDate, Today(now)));
        if (gate.IsPassed) jobs.AddRange(PassedFirstPages(source, row, now));
        if (!row.HasSummariesChain && source.EnabledBillTextSource() is not null)
            jobs.Add(source.CreateFirstPageJob(ApiResource.BillTextVersions, IdentityOf(bill), bill.UpdateDate, Today(now)));
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

    private static IEnumerable<ApiJob> PassedFirstPages(ConfiguredCollectionSource source, BillGateRow gate,
        DateTimeOffset now)
    {
        yield return source.CreateFirstPageJob(ApiResource.BillActions, IdentityOf(gate), UpdateDateOf(gate), Today(now));
        if (gate.HasSummariesChain)
            yield return source.CreateFirstPageJob(ApiResource.BillSummaries, IdentityOf(gate), UpdateDateOf(gate), Today(now));
    }

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
            CosponsorCount = bill.CosponsorCount,
            SummaryCount = bill.SummaryCount,
            SubjectCount = bill.SubjectCount,
            LatestActionRead = true,
            LatestActionDate = bill.LatestAction?.Date,
            LatestActionText = bill.LatestAction?.Text,
            IssueRevisionId = issueRevisionId,
            HasTrackedIssue = gate.HasTrackedIssue,
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
