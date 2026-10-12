using CivicLens.Infrastructure.Collection.Persistence;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementCandidateRowConfiguration : IEntityTypeConfiguration<StatementCandidateRow>
{
    public void Configure(EntityTypeBuilder<StatementCandidateRow> builder)
    {
        builder.ToTable("statement_candidates", table =>
            table.HasCheckConstraint("ck_statement_candidates_id", "candidate_id ~ '^[0-9a-f]{64}$'"));
        builder.HasKey(row => row.CandidateId);
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(row => row.Url).HasColumnName("url").HasMaxLength(4096).IsRequired();
        builder.Property(row => row.ExtractionId).HasColumnName("extraction_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.HasOne<DocumentExtractionRow>().WithMany().HasForeignKey(row => row.ExtractionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttemptRow>().WithMany().HasForeignKey(row => row.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
