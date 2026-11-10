using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseBillCountRowConfiguration : IEntityTypeConfiguration<ReleaseBillCountRow>
{
    public void Configure(EntityTypeBuilder<ReleaseBillCountRow> builder)
    {
        builder.ToTable("release_bill_counts", table =>
        {
            table.HasCheckConstraint("ck_release_bill_counts_congress", "congress > 0");
            table.HasCheckConstraint("ck_release_bill_counts_bills", "bills >= 0");
            table.HasCheckConstraint("ck_release_bill_counts_expected_member_row_count", "expected_member_row_count >= 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.Congress });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.Bills).HasColumnName("bills");
        builder.Property(row => row.ExpectedMemberRowCount).HasColumnName("expected_member_row_count");
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
    }
}
