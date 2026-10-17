using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementGateLabelRowConfiguration : IEntityTypeConfiguration<StatementGateLabelRow>
{
    public void Configure(EntityTypeBuilder<StatementGateLabelRow> builder)
    {
        builder.ToTable("statement_gate_labels", table =>
        {
            table.HasCheckConstraint("ck_statement_gate_labels_id", "label_id ~ '^[0-9a-f]{32}$'");
            table.HasCheckConstraint("ck_statement_gate_labels_author_kind", "author_kind IN ('Person','Analysis')");
            table.HasCheckConstraint("ck_statement_gate_labels_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
        });
        builder.HasKey(row => row.LabelId);
        builder.Property(row => row.LabelId).HasColumnName("label_id").HasMaxLength(32);
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.ExtractionId).HasColumnName("extraction_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.LabelJson).HasColumnName("label_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.HasIndex(row => new { row.CandidateId, row.CreatedAtUtcTicks });
        builder.HasOne<StatementCandidateRow>().WithMany().HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}
