using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Infrastructure.Collection.BillStream;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.Jobs;

public sealed partial class PostgresCollectionJobStore
{
    private sealed record PageState(BillPageRole Role, int Order, bool ClosesChain);

    internal static string HashActionCandidate(string sourceId, int congress, string billType, int billNumber,
        long updateDateUtcTicks)
    {
        var identity = $"{sourceId.Length}:{sourceId}{congress}:{billType}:{billNumber}:{updateDateUtcTicks}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

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
        if (!belongs) throw new InvalidDataException("The extraction does not belong to an attempt of the bill page job.");
    }

    private static async Task RecordActionCandidateAsync(CollectionAttemptDbContext db, ConfiguredCollectionSource source,
        BillGateRow gate, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (gate.Decision != BillGateDecision.Passed) return;
        await db.SaveChangesAsync(cancellationToken);
        var pages = await db.Set<BillPageRow>().Where(row => row.DetailJobId == gate.DetailJobId)
            .Select(row => new PageState(row.Role, row.PageOrder, row.ClosesChain)).ToListAsync(cancellationToken);
        var candidate = await db.Set<ActionCandidateRow>().SingleOrDefaultAsync(
            row => row.DetailJobId == gate.DetailJobId, cancellationToken);
        if (candidate is null)
        {
            if (!HasCompleteEvidence(gate, pages)) return;
            candidate = NewActionCandidate(source, gate, now);
            db.Add(candidate);
        }
        if (candidate.SummariesCompletedAtUtcTicks is null && IsClosed(pages, BillPageRole.Summaries))
            candidate.SummariesCompletedAtUtcTicks = Ticks(now);
    }

    private static bool HasCompleteEvidence(BillGateRow gate, IReadOnlyList<PageState> pages) =>
        IsClosed(pages, BillPageRole.Detail) && IsClosed(pages, BillPageRole.Actions) &&
        (!gate.HasCosponsorChain || IsClosed(pages, BillPageRole.Cosponsors));

    private static bool IsClosed(IReadOnlyList<PageState> pages, BillPageRole role)
    {
        var chain = pages.Where(page => page.Role == role).ToList();
        var closing = chain.Where(page => page.ClosesChain).ToList();
        return closing.Count == 1 && closing[0].Order == chain.Count - 1 && chain.Max(page => page.Order) == chain.Count - 1;
    }

    private static ActionCandidateRow NewActionCandidate(ConfiguredCollectionSource source, BillGateRow gate, DateTimeOffset now) => new()
    {
        CandidateId = HashActionCandidate(gate.SourceId, gate.Congress, gate.BillType, gate.BillNumber, gate.UpdateDateUtcTicks),
        SourceId = gate.SourceId,
        Congress = gate.Congress,
        BillType = gate.BillType,
        BillNumber = gate.BillNumber,
        UpdateDateUtcTicks = gate.UpdateDateUtcTicks,
        DetailJobId = gate.DetailJobId,
        ConfigurationRevisionId = source.Revision.Id,
        CreatedAtUtcTicks = Ticks(now)
    };
}
