using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementDraftRowConfiguration : IEntityTypeConfiguration<StatementDraftRow>
{
    public void Configure(EntityTypeBuilder<StatementDraftRow> builder)
    {
        builder.ToTable("statement_drafts", table =>
            table.HasCheckConstraint("ck_statement_drafts_revision", "current_revision_number >= 0"));
        builder.HasKey(row => row.CandidateId);
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.CurrentRevisionNumber).HasColumnName("current_revision_number");
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.HasOne<StatementCandidateRow>().WithMany().HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}
