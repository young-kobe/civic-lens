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
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.IssueId });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Name).HasColumnName("name").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Definition).HasColumnName("definition");
        builder.HasOne<PublicationReleaseRow>().WithMany().HasForeignKey(row => row.ReleaseNumber).OnDelete(DeleteBehavior.Restrict);
    }
}
