using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseIssuePartyCountRowConfiguration : IEntityTypeConfiguration<ReleaseIssuePartyCountRow>
{
    public void Configure(EntityTypeBuilder<ReleaseIssuePartyCountRow> builder)
    {
        builder.ToTable("release_issue_party_counts", table =>
        {
            table.HasCheckConstraint("ck_release_issue_party_counts_party", "length(btrim(party)) > 0");
            table.HasCheckConstraint("ck_release_issue_party_counts_bills", "bills > 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.IssueId, row.Party });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Party).HasColumnName("party").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Bills).HasColumnName("bills");
        builder.HasOne<ReleaseIssuePartyLineupRow>().WithMany()
            .HasForeignKey(row => new { row.ReleaseNumber, row.IssueId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_release_issue_party_counts_lineup");
    }
}
