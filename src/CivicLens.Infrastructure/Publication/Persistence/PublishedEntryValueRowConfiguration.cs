using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal abstract class PublishedEntryValueRowConfiguration<TRow>(string table, string column) : IEntityTypeConfiguration<TRow>
    where TRow : PublishedEntryValueRow
{
    public void Configure(EntityTypeBuilder<TRow> builder)
    {
        builder.ToTable(table, check =>
        {
            check.HasCheckConstraint($"ck_{table}_position", "position >= 0");
            check.HasCheckConstraint($"ck_{table}_{column}", $"length(btrim({column})) > 0");
        });
        builder.HasKey(row => new { row.Kind, row.RecordId, row.RevisionNumber, row.Position });
        builder.HasAlternateKey(row => new { row.Kind, row.RecordId, row.RevisionNumber, row.Value });
        builder.Property(row => row.Kind).HasColumnName("kind").HasMaxLength(32);
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Value).HasColumnName(column).HasMaxLength(PublishedRecordEntry.MaximumFilterValueLength);
        builder.HasOne<PublishedEntryRow>().WithMany().HasForeignKey(row => new { row.Kind, row.RecordId, row.RevisionNumber })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PublishedEntryIssueRowConfiguration() : PublishedEntryValueRowConfiguration<PublishedEntryIssueRow>("published_entry_issues", "issue_id");

internal sealed class PublishedEntryPolicyAreaRowConfiguration()
    : PublishedEntryValueRowConfiguration<PublishedEntryPolicyAreaRow>("published_entry_policy_areas", "policy_area_id");
