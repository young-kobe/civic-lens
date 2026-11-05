using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal abstract class ReleaseIssueTermRowConfiguration<TRow>(string table) : IEntityTypeConfiguration<TRow> where TRow : ReleaseIssueTermRow
{
    public void Configure(EntityTypeBuilder<TRow> builder)
    {
        builder.ToTable(table, check =>
        {
            check.HasCheckConstraint($"ck_{table}_position", "position >= 0");
            check.HasCheckConstraint($"ck_{table}_text", "length(btrim(text)) > 0");
        });
        builder.HasKey(row => new { row.ReleaseNumber, row.IssueId, row.Position });
        builder.HasAlternateKey(row => new { row.ReleaseNumber, row.IssueId, row.Text });
        builder.Property(row => row.ReleaseNumber).HasColumnName("release_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Text).HasColumnName("text");
        builder.HasOne<ReleaseIssueRow>().WithMany().HasForeignKey(row => new { row.ReleaseNumber, row.IssueId }).OnDelete(DeleteBehavior.Restrict);
    }
}
