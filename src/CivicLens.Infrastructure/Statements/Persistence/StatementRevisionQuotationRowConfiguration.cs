using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionQuotationRowConfiguration : IEntityTypeConfiguration<StatementRevisionQuotationRow>
{
    public void Configure(EntityTypeBuilder<StatementRevisionQuotationRow> builder)
    {
        builder.ToTable("statement_revision_quotations", table =>
        {
            table.HasCheckConstraint("ck_statement_revision_quotations_position", $"position BETWEEN 0 AND {StatementDraftRevision.MaximumQuotations - 1}");
            table.HasCheckConstraint("ck_statement_revision_quotations_length", $"quotation_length <= {StatementDraftRevision.MaximumQuotationLength}");
            table.HasCheckConstraint("ck_statement_revision_quotations_kind", EnumChecks.InList("kind", Enum.GetNames<StatementQuotationKind>()));
        });
        builder.HasKey(row => new { row.CandidateId, row.RevisionNumber, row.Position });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(16);
        SourceSpanColumns.Configure(builder, "statement_revision_quotations", "Quotation", "quotation", optional: false);
        builder.HasIndex(row => new { row.CandidateId, row.RevisionNumber, row.QuotationExtractionId, row.QuotationStart, row.QuotationLength }).IsUnique();
        builder.HasOne<StatementCandidateRow>().WithMany().HasForeignKey(row => new { row.CandidateId, row.QuotationExtractionId })
            .HasPrincipalKey(candidate => new { candidate.CandidateId, candidate.ExtractionId }).OnDelete(DeleteBehavior.Restrict);
    }
}
