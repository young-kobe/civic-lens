using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

internal static class BillHistoryCaptures
{
    internal const string CompleteChainSql = """
                          SELECT max(c.observed_at_utc_ticks) AS observed_at_utc_ticks
                            FROM bill_pages p
                            LEFT JOIN document_extractions e ON e.extraction_id = p.extraction_id
                            LEFT JOIN collection_attempts c ON c.attempt_id = e.attempt_id AND c.outcome = 'captured'
                                                           AND c.capture_sha256 IS NOT NULL
                           WHERE p.detail_job_id = g.detail_job_id AND p.role = 'Actions'
                          HAVING count(*) > 0 AND min(p.page_order) = 0 AND max(p.page_order) = count(*) - 1
                             AND count(*) FILTER (WHERE p.closes_chain) = 1
                             AND max(p.page_order) FILTER (WHERE p.closes_chain) = count(*) - 1
                             AND count(c.attempt_id) = count(*)
        """;

    public static async Task<BillHistoryCapture?> ReadCompleteAsync(CollectionAttemptDbContext db, string detailJobId,
        CancellationToken cancellationToken)
    {
        var row = await db.Database.SqlQueryRaw<CaptureRow>($$"""
            SELECT g.detail_job_id AS "DetailJobId", g.source_id AS "SourceId", g.congress AS "Congress",
                   g.bill_type AS "BillType", g.bill_number AS "BillNumber",
                   chain.observed_at_utc_ticks AS "ObservedAtUtcTicks",
                   (SELECT count(*)::integer FROM bill_actions a WHERE a.detail_job_id = g.detail_job_id) AS "ActionCount"
              FROM bill_gates g
              JOIN LATERAL ({{CompleteChainSql}}) chain ON true
             WHERE g.detail_job_id = {0}
            """, detailJobId).SingleOrDefaultAsync(cancellationToken);
        return row is null ? null : new(row.DetailJobId, row.SourceId, row.Congress, row.BillType, row.BillNumber,
            new DateTimeOffset(row.ObservedAtUtcTicks, TimeSpan.Zero), row.ActionCount);
    }

    private sealed record CaptureRow(string DetailJobId, string SourceId, int Congress, string BillType, int BillNumber,
        long ObservedAtUtcTicks, int ActionCount);
}
