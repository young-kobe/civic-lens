using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal static class BillDetailRows
{
    public static IQueryable<BillPageRow> PagesNewestFirst(CollectionAttemptDbContext db, int congress, string billType, int billNumber) =>
        db.Set<BillGateRow>().Where(gate => gate.Congress == congress && gate.BillType == billType && gate.BillNumber == billNumber)
            .Join(db.Set<BillPageRow>().Where(page => page.Role == BillPageRole.Detail), gate => gate.DetailJobId, page => page.JobId,
                (gate, page) => new { gate.UpdateDateUtcTicks, Page = page })
            .OrderByDescending(detail => detail.UpdateDateUtcTicks).ThenByDescending(detail => detail.Page.JobId).Select(detail => detail.Page);

    public static IQueryable<BillPageRow> SubjectPagesOfNewestVersion(CollectionAttemptDbContext db, int congress, string billType, int billNumber)
    {
        var newestClosedChain = db.Set<BillGateRow>().Where(gate => gate.Congress == congress && gate.BillType == billType && gate.BillNumber == billNumber)
            .Join(db.Set<BillPageRow>().Where(page => page.Role == BillPageRole.Subjects && page.ClosesChain), gate => gate.DetailJobId, page => page.DetailJobId,
                (gate, page) => new { gate.UpdateDateUtcTicks, gate.DetailJobId })
            .OrderByDescending(chain => chain.UpdateDateUtcTicks).ThenByDescending(chain => chain.DetailJobId).Select(chain => chain.DetailJobId).Take(1);
        return db.Set<BillPageRow>().Where(page => page.Role == BillPageRole.Subjects && newestClosedChain.Contains(page.DetailJobId))
            .OrderBy(page => page.PageOrder);
    }
}
