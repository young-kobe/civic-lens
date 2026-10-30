using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveActionDocumentRowConfiguration : IEntityTypeConfiguration<ExecutiveActionDocumentRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveActionDocumentRow> builder)
    {
        builder.ToTable("executive_action_documents", table =>
        {
            table.HasCheckConstraint("ck_executive_action_documents_revision", "revision_number >= 1");
            table.HasCheckConstraint("ck_executive_action_documents_pending", "revision_number IS NOT NULL OR notice_json IS NOT NULL");
        });
        builder.HasKey(row => new { row.SourceId, row.DocumentNumber });
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.DocumentNumber).HasColumnName("document_number").HasMaxLength(32);
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.NoticeJson).HasColumnName("notice_json");
        builder.Property(row => row.PublishedOn).HasColumnName("published_on").HasColumnType("date");
        builder.Property(row => row.DocumentJobId).HasColumnName("document_job_id").HasMaxLength(128).IsRequired();
        builder.HasIndex(row => new { row.SourceId, row.PublishedOn }).HasDatabaseName("ix_executive_action_documents_published");
        builder.HasOne<ExecutiveActionRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.RecordId, row.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.DocumentJobId).OnDelete(DeleteBehavior.Restrict);
    }
}
