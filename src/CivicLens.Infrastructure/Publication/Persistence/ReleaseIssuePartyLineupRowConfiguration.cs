using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseIssuePartyLineupRowConfiguration : IEntityTypeConfiguration<ReleaseIssuePartyLineupRow>
{
    public void Configure(EntityTypeBuilder<ReleaseIssuePartyLineupRow> builder)
    {
        builder.ToTable("release_issue_party_lineups", table =>
            table.HasCheckConstraint("ck_release_issue_party_lineups_unknown_bills", "unknown_bills >= 0"));
        builder.HasKey(row => new { row.ReleaseNumber, row.IssueId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.UnknownBills).HasColumnName("unknown_bills");
        builder.HasOne<ReleaseIssueRow>().WithMany()
            .HasForeignKey(row => new { row.ReleaseNumber, row.IssueId }).OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_release_issue_party_lineups_issue");
    }
}
