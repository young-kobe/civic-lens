using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionSpeakerRowConfiguration : IEntityTypeConfiguration<StatementRevisionSpeakerRow>
{
    public void Configure(EntityTypeBuilder<StatementRevisionSpeakerRow> builder)
    {
        builder.ToTable("statement_revision_speakers", table =>
        {
            table.HasCheckConstraint("ck_statement_revision_speakers_position", $"position BETWEEN 0 AND {StatementDraftRevision.MaximumSelections - 1}");
            table.HasCheckConstraint("ck_statement_revision_speakers_official", "length(btrim(official_id)) > 0");
        });
        builder.HasKey(row => new { row.CandidateId, row.RevisionNumber, row.OfficialId });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.OfficialId).HasColumnName("official_id").HasMaxLength(ReviewTextRules.MaximumConfigurationIdLength);
        builder.Property(row => row.Position).HasColumnName("position");
    }
}
