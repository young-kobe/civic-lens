using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal static class BillDetailRows
{
    public static IQueryable<BillPageRow> PagesNewestFirst(CollectionAttemptDbContext db, int congress, string billType, int billNumber) =>
        db.Set<BillGateRow>().Where(gate => gate.Congress == congress && gate.BillType == billType && gate.BillNumber == billNumber)
            .Join(db.Set<BillPageRow>().Where(page => page.Role == BillPageRole.Detail), gate => gate.DetailJobId, page => page.JobId,
                (gate, page) => new { gate.UpdateDateUtcTicks, Page = page })
            .OrderByDescending(detail => detail.UpdateDateUtcTicks).Select(detail => detail.Page);
}
