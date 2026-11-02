using CivicLens.Infrastructure.Analysis.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipAnalysisRowConfiguration : IEntityTypeConfiguration<RelationshipAnalysisRow>
{
    public void Configure(EntityTypeBuilder<RelationshipAnalysisRow> builder)
    {
        AnalysisTableConfiguration.ConfigureQueue<RelationshipAnalysisRow, RelationshipAnalysisRunRow>(builder, "relationship_analysis", nameof(RelationshipAnalysisRow.PairId), "pair_id");
        builder.ToTable("relationship_analysis", table =>
        {
            table.HasCheckConstraint("ck_relationship_analysis_pair", "pair_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_relationship_analysis_target_kind", "target_kind IN ('Action','Vote')");
        });
        builder.Property(row => row.StatementCandidateId).HasColumnName("statement_candidate_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.TargetKind).HasColumnName("target_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.TargetRecordId).HasColumnName("target_record_id").HasMaxLength(64).IsRequired();
        builder.HasOne<StatementCandidateRow>().WithMany()
            .HasForeignKey(row => row.StatementCandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}
