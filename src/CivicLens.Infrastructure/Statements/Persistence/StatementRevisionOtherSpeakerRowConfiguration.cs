using CivicLens.Core.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionOtherSpeakerRowConfiguration : IEntityTypeConfiguration<StatementRevisionOtherSpeakerRow>
{
    public void Configure(EntityTypeBuilder<StatementRevisionOtherSpeakerRow> builder)
    {
        builder.ToTable("statement_revision_other_speakers", table =>
        {
            table.HasCheckConstraint("ck_statement_revision_other_speakers_position", $"position BETWEEN 0 AND {StatementDraftRevision.MaximumOtherSpeakers - 1}");
            table.HasCheckConstraint("ck_statement_revision_other_speakers_name",
                $"length(speaker_name) BETWEEN 1 AND {StatementDraftRevision.MaximumOtherSpeakerLength} AND speaker_name = btrim(speaker_name)");
        });
        builder.HasKey(row => new { row.CandidateId, row.RevisionNumber, row.Position });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.SpeakerName).HasColumnName("speaker_name").HasMaxLength(StatementDraftRevision.MaximumOtherSpeakerLength);
        builder.HasIndex(row => new { row.CandidateId, row.RevisionNumber, row.SpeakerName }).IsUnique();
    }
}
