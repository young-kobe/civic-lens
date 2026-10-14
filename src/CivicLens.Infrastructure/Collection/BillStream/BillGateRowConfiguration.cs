using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillGateRowConfiguration : IEntityTypeConfiguration<BillGateRow>
{
    public void Configure(EntityTypeBuilder<BillGateRow> builder)
    {
        builder.ToTable("bill_gates", table =>
        {
            table.HasCheckConstraint("ck_bill_gates_bill", "congress >= 1 AND bill_number >= 1");
            table.HasCheckConstraint("ck_bill_gates_cosponsor_count", "cosponsor_count >= 0");
            table.HasCheckConstraint("ck_bill_gates_summary_count", "summary_count >= 0");
            table.HasCheckConstraint("ck_bill_gates_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
            table.HasCheckConstraint("ck_bill_gates_created", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
            table.HasCheckConstraint("ck_bill_gates_decided",
                "(decision = 'Pending') = (decided_at_utc_ticks IS NULL) AND (decided_at_utc_ticks IS NULL OR decided_at_utc_ticks BETWEEN 0 AND 3155378975999999999)");
            table.HasCheckConstraint("ck_bill_gates_issue_revision", "issue_revision_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_bill_gates_reason",
                "(decision = 'Pending' AND reason IS NULL) OR " +
                "(decision = 'Passed' AND reason IS NOT NULL AND reason IN ('CoveredSponsor','TrackedPolicyArea','CoveredCosponsor')) OR " +
                "(decision = 'NotRelevant' AND reason IS NOT NULL AND reason IN ('NoPolicyArea','UntrackedPolicyArea'))");
        });
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.BillType).HasColumnName("bill_type").HasMaxLength(16);
        builder.Property(row => row.BillNumber).HasColumnName("bill_number");
        builder.Property(row => row.UpdateDateUtcTicks).HasColumnName("update_date_utc_ticks");
        builder.Property(row => row.PolicyArea).HasColumnName("policy_area");
        builder.Property(row => row.CosponsorCount).HasColumnName("cosponsor_count");
        builder.Property(row => row.SummaryCount).HasColumnName("summary_count");
        builder.Property(row => row.IssueRevisionId).HasColumnName("issue_revision_id").HasMaxLength(64);
        builder.Property(row => row.Decision).HasColumnName("decision").HasMaxLength(32)
            .HasConversion<string>();
        builder.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(32)
            .HasConversion<string>();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.DecidedAtUtcTicks).HasColumnName("decided_at_utc_ticks");
        builder.HasKey(row => row.DetailJobId);
        builder.HasIndex(row => new { row.SourceId, row.Congress, row.BillType, row.BillNumber, row.UpdateDateUtcTicks })
            .IsUnique().HasDatabaseName("ux_bill_gates_bill_version");
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.DetailJobId).OnDelete(DeleteBehavior.Restrict);
    }
}
