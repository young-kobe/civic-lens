using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.HouseVotes;

internal sealed class HouseVoteRowConfiguration : IEntityTypeConfiguration<HouseVoteRow>
{
    public void Configure(EntityTypeBuilder<HouseVoteRow> builder)
    {
        builder.ToTable("house_votes", table =>
        {
            table.HasCheckConstraint("ck_house_votes_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
            table.HasCheckConstraint("ck_house_votes_bill",
                "(bill_type IS NULL) = (bill_number IS NULL) AND (bill_number IS NULL OR bill_number >= 1)");
            table.HasCheckConstraint("ck_house_votes_update", "update_date_utc_ticks BETWEEN 0 AND 3155378975999999999");
        });
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.SessionNumber).HasColumnName("session_number");
        builder.Property(row => row.RollCallNumber).HasColumnName("roll_call_number");
        builder.Property(row => row.UpdateDateUtcTicks).HasColumnName("update_date_utc_ticks");
        builder.Property(row => row.BillType).HasColumnName("bill_type").HasMaxLength(16);
        builder.Property(row => row.BillNumber).HasColumnName("bill_number");
        builder.Property(row => row.ExtractionId).HasColumnName("extraction_id").HasMaxLength(64);
        builder.HasKey(row => row.DetailJobId);
        builder.HasIndex(row => new { row.SourceId, row.Congress, row.SessionNumber, row.RollCallNumber, row.UpdateDateUtcTicks })
            .IsUnique().HasDatabaseName("ux_house_votes_version");
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.DetailJobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentExtractionRow>().WithMany().HasForeignKey(row => row.ExtractionId).OnDelete(DeleteBehavior.Restrict);
    }
}
