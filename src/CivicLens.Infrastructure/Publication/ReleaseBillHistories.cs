using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication.Persistence;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Publication;

internal static class ReleaseBillHistories
{
    public static async Task RequireAndAddAsync(CollectionAttemptDbContext db, PublicationCommit commit, CancellationToken cancellationToken)
    {
        if (commit.BillHistories.IsEmpty) return;
        if (commit.BillHistories.Any(binding => binding is null) ||
            commit.BillHistories.Select(binding => binding.RecordId).Distinct(StringComparer.Ordinal).Count() != commit.BillHistories.Length)
            throw new ArgumentException("A release binds one history per bill.", nameof(commit));
        var actions = commit.Records.Where(record => record.Kind == PublishedRecordKind.Action)
            .ToDictionary(record => record.RecordId, StringComparer.Ordinal);
        if (commit.BillHistories.Any(binding => !actions.ContainsKey(binding.RecordId) ||
                binding.ProjectionVersion != BillHistoryCapture.CurrentProjectionVersion))
            throw new ArgumentException("A release can bind only current verified history for a bill it publishes.", nameof(commit));
        var bindings = commit.BillHistories;
        var matching = await db.Database.SqlQueryRaw<int>("""
            SELECT count(*)::integer AS "Value"
              FROM unnest({0}::text[], {1}::integer[], {2}::text[], {3}::integer[])
                   binding(record_id, revision_number, detail_job_id, projection_version)
              JOIN action_revisions r ON r.record_id = binding.record_id AND r.revision_number = binding.revision_number
              JOIN published_bill_histories h ON h.detail_job_id = binding.detail_job_id AND h.projection_version = binding.projection_version
                   AND h.source_id = r.source_id AND h.congress = r.congress
                   AND h.bill_type = r.bill_type AND h.bill_number = r.bill_number
            """, bindings.Select(binding => binding.RecordId).ToArray(),
                bindings.Select(binding => actions[binding.RecordId].RevisionNumber).ToArray(),
                bindings.Select(binding => binding.DetailJobId).ToArray(),
                bindings.Select(binding => binding.ProjectionVersion).ToArray()).SingleAsync(cancellationToken);
        if (matching != bindings.Length)
            throw new PublicationConflictException("A selected history copy is absent or belongs to another bill revision.");
        db.AddRange(bindings.Select(binding => new ReleaseBillHistoryRow
        {
            ReleaseNumber = commit.ReleaseNumber,
            RecordId = binding.RecordId,
            DetailJobId = binding.DetailJobId,
            ProjectionVersion = binding.ProjectionVersion
        }));
    }
}
