using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class ActionCandidateRowConfiguration : IEntityTypeConfiguration<ActionCandidateRow>
{
    public void Configure(EntityTypeBuilder<ActionCandidateRow> builder)
    {
        builder.ToTable("action_candidates", table =>
        {
            table.HasCheckConstraint("ck_action_candidates_id", "candidate_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_action_candidates_bill", "congress >= 1 AND bill_number >= 1");
            table.HasCheckConstraint("ck_action_candidates_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
            table.HasCheckConstraint("ck_action_candidates_created", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
        });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.BillType).HasColumnName("bill_type").HasMaxLength(16);
        builder.Property(row => row.BillNumber).HasColumnName("bill_number");
        builder.Property(row => row.UpdateDateUtcTicks).HasColumnName("update_date_utc_ticks");
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.Property(row => row.ConfigurationRevisionId).HasColumnName("configuration_revision_id").HasMaxLength(64);
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.IncludesSummaries).HasColumnName("includes_summaries");
        builder.Property(row => row.IncludesSubjects).HasColumnName("includes_subjects");
        builder.HasKey(row => row.CandidateId);
        builder.HasIndex(row => new { row.DetailJobId, row.IncludesSummaries, row.IncludesSubjects }).IsUnique().HasDatabaseName("ux_action_candidates_detail_job");
        builder.HasIndex(row => new { row.SourceId, row.Congress, row.BillType, row.BillNumber, row.UpdateDateUtcTicks })
            .HasDatabaseName("ix_action_candidates_bill");
        builder.HasOne<BillGateRow>().WithMany().HasForeignKey(row => row.DetailJobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationRevisionRow>().WithMany().HasForeignKey(row => row.ConfigurationRevisionId)
            .HasConstraintName("fk_action_candidates_configuration_revision").OnDelete(DeleteBehavior.Restrict);
    }
}
