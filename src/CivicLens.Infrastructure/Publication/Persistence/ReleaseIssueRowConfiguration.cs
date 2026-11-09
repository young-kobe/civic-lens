using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class ReleaseIssueRowConfiguration : IEntityTypeConfiguration<ReleaseIssueRow>
{
    public void Configure(EntityTypeBuilder<ReleaseIssueRow> builder)
    {
        builder.ToTable("release_issues", table =>
        {
            table.HasCheckConstraint("ck_release_issues_issue_id", "length(btrim(issue_id)) > 0");
            table.HasCheckConstraint("ck_release_issues_name", "length(btrim(name)) > 0");
            table.HasCheckConstraint("ck_release_issues_home_counts", "(count_congress IS NULL AND substantive_votes IS NULL AND bills IS NULL) OR (count_congress IS NOT NULL AND substantive_votes IS NOT NULL AND bills IS NOT NULL AND count_congress > 0 AND substantive_votes >= 0 AND bills >= 0)");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.IssueId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Name).HasColumnName("name").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Definition).HasColumnName("definition");
        builder.Property(row => row.CountCongress).HasColumnName("count_congress");
        builder.Property(row => row.SubstantiveVotes).HasColumnName("substantive_votes");
        builder.Property(row => row.Bills).HasColumnName("bills");
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
    }
}
