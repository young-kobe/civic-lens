using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Infrastructure.Actions;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Statements;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore
{
    private sealed record PageState(BillPageRole Role, int Order, bool ClosesChain);

    internal static string HashActionCandidate(string sourceId, int congress, string billType, int billNumber,
        long updateDateUtcTicks, bool includesSummaries) =>
        HashIdentity($"{sourceId.Length}:{sourceId}{congress}:{billType}:{billNumber}:{updateDateUtcTicks}" +
            (includesSummaries ? ":summaries" : ""));

    internal static string HashActionRecord(string sourceId, int congress, string billType, int billNumber) =>
        HashIdentity($"{sourceId.Length}:{sourceId}{congress}:{billType}:{billNumber}");

    private static BillPageRow NewPageRow(string detailJobId, string jobId, BillPageRole role, int order, string extractionId,
        bool closesChain) => new()
        {
            JobId = jobId,
            DetailJobId = detailJobId,
            Role = role,
            PageOrder = order,
            ExtractionId = extractionId,
            ClosesChain = closesChain
        };

    private static async Task RecordPageAsync(CollectionAttemptDbContext db, string detailJobId, string jobId,
        BillPageRole role, string extractionId, bool closesChain, CancellationToken cancellationToken)
    {
        if (await db.Set<BillPageRow>().AnyAsync(row => row.JobId == jobId, cancellationToken)) return;
        await EnsureExtractionIsOfJobAsync(db, jobId, extractionId, cancellationToken);
        var order = await db.Set<BillPageRow>().CountAsync(row => row.DetailJobId == detailJobId && row.Role == role,
            cancellationToken);
        db.Add(NewPageRow(detailJobId, jobId, role, order, extractionId, closesChain));
    }

    private static async Task EnsureExtractionIsOfJobAsync(CollectionAttemptDbContext db, string jobId, string extractionId,
        CancellationToken cancellationToken)
    {
        var belongs = await db.Set<JobAttemptRow>().AnyAsync(attempt => attempt.JobId == jobId &&
            db.Set<DocumentExtractionRow>().Any(extraction => extraction.ExtractionId == extractionId &&
                extraction.AttemptId == attempt.AttemptId), cancellationToken);
        if (!belongs) throw new InvalidDataException("The extraction does not belong to an attempt of the job.");
    }

    private static async Task RecordActionCandidateAsync(CollectionAttemptDbContext db, ConfiguredCollectionSource source,
        BillGateRow gate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (gate.Decision != BillGateDecision.Passed) return;
        await db.SaveChangesAsync(cancellationToken);
        var pages = await db.Set<BillPageRow>().Where(row => row.DetailJobId == gate.DetailJobId)
            .Select(row => new PageState(row.Role, row.PageOrder, row.ClosesChain)).ToListAsync(cancellationToken);
        if (!HasCompleteEvidence(gate, pages)) return;
        var includesSummaries = IsClosed(pages, BillPageRole.Summaries);
        if (await HasCandidateWithAtLeastTheseInputsAsync(db, gate.DetailJobId, includesSummaries, cancellationToken)) return;
        var candidate = NewActionCandidate(source, gate, includesSummaries, now);
        db.Add(candidate);
        var priority = await StatementBillMentions.PriorityOfBillAsync(db, gate.Congress, gate.BillType, gate.BillNumber, gate.HasTrackedIssue,
            cancellationToken);
        ActionDraftQueueRows.Add(db, candidate.CandidateId, Ticks(now), priority);
    }

    private static Task<bool> HasCandidateWithAtLeastTheseInputsAsync(CollectionAttemptDbContext db, string detailJobId,
        bool includesSummaries, CancellationToken cancellationToken) =>
        db.Set<ActionCandidateRow>().AnyAsync(row => row.DetailJobId == detailJobId &&
            (row.IncludesSummaries || row.IncludesSummaries == includesSummaries), cancellationToken);

    private static bool HasCompleteEvidence(BillGateRow gate, IReadOnlyList<PageState> pages) =>
        IsClosed(pages, BillPageRole.Detail) && IsClosed(pages, BillPageRole.Actions) &&
        (!gate.HasCosponsorChain || IsClosed(pages, BillPageRole.Cosponsors));

    private static bool IsClosed(IReadOnlyList<PageState> pages, BillPageRole role)
    {
        var chain = pages.Where(page => page.Role == role).ToList();
        var closing = chain.Where(page => page.ClosesChain).ToList();
        return closing.Count == 1 && closing[0].Order == chain.Count - 1 && chain.Max(page => page.Order) == chain.Count - 1;
    }

    private static ActionCandidateRow NewActionCandidate(ConfiguredCollectionSource source, BillGateRow gate, bool includesSummaries,
        DateTimeOffset now) => new()
        {
            CandidateId = HashActionCandidate(gate.SourceId, gate.Congress, gate.BillType, gate.BillNumber, gate.UpdateDateUtcTicks,
                includesSummaries),
            SourceId = gate.SourceId,
            Congress = gate.Congress,
            BillType = gate.BillType,
            BillNumber = gate.BillNumber,
            UpdateDateUtcTicks = gate.UpdateDateUtcTicks,
            DetailJobId = gate.DetailJobId,
            ConfigurationRevisionId = source.Revision.Id,
            CreatedAtUtcTicks = Ticks(now),
            IncludesSummaries = includesSummaries
        };
}
