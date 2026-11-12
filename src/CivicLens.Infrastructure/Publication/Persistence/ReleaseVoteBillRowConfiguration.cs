using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseVoteBillRowConfiguration : IEntityTypeConfiguration<ReleaseVoteBillRow>
{
    public void Configure(EntityTypeBuilder<ReleaseVoteBillRow> builder)
    {
        builder.ToTable("release_vote_bills", table =>
        {
            table.HasCheckConstraint("ck_release_vote_bills_vote_record_id", "vote_record_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_release_vote_bills_bill_record_id", "bill_record_id ~ '^[0-9a-f]{64}$'");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.VoteRecordId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.VoteRecordId).HasColumnName("vote_record_id").HasMaxLength(64);
        builder.Property(row => row.BillRecordId).HasColumnName("bill_record_id").HasMaxLength(64);
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
    }
}
