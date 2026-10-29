using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Votes;

internal sealed class VoteCandidateRowConfiguration : IEntityTypeConfiguration<VoteCandidateRow>
{
    public void Configure(EntityTypeBuilder<VoteCandidateRow> builder)
    {
        builder.ToTable("vote_candidates", table =>
        {
            table.HasCheckConstraint("ck_vote_candidates_id", "candidate_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_vote_candidates_chamber", "chamber IN ('House', 'Senate')");
            table.HasCheckConstraint("ck_vote_candidates_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
            table.HasCheckConstraint("ck_vote_candidates_bill",
                "(bill_type IS NULL) = (bill_number IS NULL) AND (bill_number IS NULL OR bill_number >= 1)");
            table.HasCheckConstraint("ck_vote_candidates_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
            table.HasCheckConstraint("ck_vote_candidates_created", "created_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
        });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.Chamber).HasColumnName("chamber").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.SessionNumber).HasColumnName("session_number");
        builder.Property(row => row.RollCallNumber).HasColumnName("roll_call_number");
        builder.Property(row => row.UpdateDateUtcTicks).HasColumnName("update_date_utc_ticks");
        builder.Property(row => row.BillType).HasColumnName("bill_type").HasMaxLength(16);
        builder.Property(row => row.BillNumber).HasColumnName("bill_number");
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.Property(row => row.MembersJobId).HasColumnName("members_job_id").HasMaxLength(128);
        builder.Property(row => row.RosterJobId).HasColumnName("roster_job_id").HasMaxLength(128);
        builder.Property(row => row.DetailExtractionId).HasColumnName("detail_extraction_id").HasMaxLength(64);
        builder.Property(row => row.MembersExtractionId).HasColumnName("members_extraction_id").HasMaxLength(64);
        builder.Property(row => row.ConfigurationRevisionId).HasColumnName("configuration_revision_id").HasMaxLength(64);
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.HasKey(row => row.CandidateId);
        builder.HasIndex(row => new { row.SourceId, row.Chamber, row.Congress, row.SessionNumber, row.RollCallNumber, row.UpdateDateUtcTicks })
            .HasDatabaseName("ix_vote_candidates_roll");
        builder.HasIndex(row => new { row.Congress, row.BillType, row.BillNumber }).HasDatabaseName("ix_vote_candidates_bill");
        builder.HasIndex(row => new { row.DetailJobId, row.SessionNumber, row.RollCallNumber }).IsUnique().HasDatabaseName("ux_vote_candidates_detail_job");
        builder.HasOne<VoteVersionRow>().WithMany().HasForeignKey(row => new { row.DetailJobId, row.SessionNumber, row.RollCallNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.MembersJobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.RosterJobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentExtractionRow>().WithMany().HasForeignKey(row => row.DetailExtractionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentExtractionRow>().WithMany().HasForeignKey(row => row.MembersExtractionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ConfigurationRevisionRow>().WithMany().HasForeignKey(row => row.ConfigurationRevisionId)
            .HasConstraintName("fk_vote_candidates_configuration_revision").OnDelete(DeleteBehavior.Restrict);
    }
}
