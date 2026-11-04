using CivicLens.Core.Briefs;
using CivicLens.Infrastructure.Actions.Persistence;
using CivicLens.Infrastructure.Review.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefRevisionCitationRowConfiguration : IEntityTypeConfiguration<BriefRevisionCitationRow>
{
    private const string Table = "brief_revision_citations";

    public void Configure(EntityTypeBuilder<BriefRevisionCitationRow> builder)
    {
        builder.ToTable(Table, table =>
        {
            table.HasCheckConstraint("ck_brief_revision_citations_part", EnumChecks.InList("part", Enum.GetNames<BriefRevisionPart>()));
            table.HasCheckConstraint("ck_brief_revision_citations_id", $"citation_id ~ '{BriefRevision.CitationIdPattern}'");
            table.HasCheckConstraint("ck_brief_revision_citations_position", $"position BETWEEN 0 AND {BriefRevision.MaximumCitations - 1}");
            table.HasCheckConstraint("ck_brief_revision_citations_kind", EnumChecks.InList("kind", Enum.GetNames<BriefCitationKind>()));
            table.HasCheckConstraint("ck_brief_revision_citations_cited",
                $"(kind = '{nameof(BriefCitationKind.Statement)}') = (statement_candidate_id IS NOT NULL) AND (statement_candidate_id IS NULL) = (statement_revision_number IS NULL) AND " +
                $"(kind = '{nameof(BriefCitationKind.Action)}') = (action_record_id IS NOT NULL) AND (action_record_id IS NULL) = (action_revision_number IS NULL) AND " +
                $"(kind = '{nameof(BriefCitationKind.DocumentChange)}') = (document_change_draft_id IS NOT NULL) AND (document_change_draft_id IS NULL) = (document_change_revision_number IS NULL) AND " +
                "coalesce(statement_revision_number, action_revision_number, document_change_revision_number) >= 1");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.Part, row.CitationId });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(BriefRevision.MaximumRecordIdLength);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Part).HasColumnName("part").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.CitationId).HasColumnName("citation_id").HasMaxLength(8);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.StatementCandidateId).HasColumnName("statement_candidate_id").HasMaxLength(BriefRevision.MaximumRecordIdLength);
        builder.Property(row => row.StatementRevisionNumber).HasColumnName("statement_revision_number");
        builder.Property(row => row.ActionRecordId).HasColumnName("action_record_id").HasMaxLength(BriefRevision.MaximumRecordIdLength);
        builder.Property(row => row.ActionRevisionNumber).HasColumnName("action_revision_number");
        builder.Property(row => row.DocumentChangeDraftId).HasColumnName("document_change_draft_id").HasMaxLength(BriefRevision.MaximumRecordIdLength);
        builder.Property(row => row.DocumentChangeRevisionNumber).HasColumnName("document_change_revision_number");
        builder.HasIndex(row => new { row.RecordId, row.RevisionNumber, row.Part, row.StatementCandidateId }).IsUnique();
        builder.HasIndex(row => new { row.RecordId, row.RevisionNumber, row.Part, row.ActionRecordId }).IsUnique();
        builder.HasIndex(row => new { row.RecordId, row.RevisionNumber, row.Part, row.DocumentChangeDraftId }).IsUnique();
        builder.HasOne<StatementRevisionRow>().WithMany().HasForeignKey(row => new { row.StatementCandidateId, row.StatementRevisionNumber })
            .HasPrincipalKey(revision => new { revision.CandidateId, revision.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ActionRevisionRow>().WithMany().HasForeignKey(row => new { row.ActionRecordId, row.ActionRevisionNumber })
            .HasPrincipalKey(revision => new { revision.RecordId, revision.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentChangeRevisionRow>().WithMany().HasForeignKey(row => new { row.DocumentChangeDraftId, row.DocumentChangeRevisionNumber })
            .HasPrincipalKey(revision => new { revision.DraftId, revision.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
    }
}
