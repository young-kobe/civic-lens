using CivicLens.Core.Statements;
using CivicLens.Infrastructure.Documents.Persistence;
using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionRowConfiguration : IEntityTypeConfiguration<StatementRevisionRow>
{
    private static readonly string[] WrittenTogether = ["headline", "summary"];

    public void Configure(EntityTypeBuilder<StatementRevisionRow> builder)
    {
        builder.ToTable("statement_revisions", table =>
        {
            table.HasCheckConstraint("ck_statement_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_statement_revisions_author_kind", "author_kind IN ('Person','Analysis')");
            table.HasCheckConstraint("ck_statement_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
            table.HasCheckConstraint("ck_statement_revisions_date_evidence", "(statement_date IS NULL) = (statement_date_evidence_extraction_id IS NULL)");
            table.HasCheckConstraint("ck_statement_revisions_date_evidence_length", $"statement_date_evidence_length <= {StatementDraftRevision.MaximumQuotationLength}");
            table.HasCheckConstraint("ck_statement_revisions_columns_written", $"num_nulls({string.Join(", ", WrittenTogether)}) IN (0, {WrittenTogether.Length})");
        });
        builder.HasKey(row => new { row.CandidateId, row.RevisionNumber });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.StatementDate).HasColumnName("statement_date").HasColumnType("date");
        HeadlineSummaryColumns.Configure(builder, "statement_revisions", StatementDraftRevision.MaximumHeadlineLength,
            StatementDraftRevision.MaximumSummaryLength, allowEmpty: true);
        SourceSpanColumns.Configure(builder, "statement_revisions", "StatementDateEvidence", "statement_date_evidence", optional: true);
        builder.HasIndex(row => row.RunId).IsUnique();
        builder.HasOne<StatementDraftRow>().WithMany().HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StatementAnalysisRunRow>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StatementCandidateRow>().WithMany().HasForeignKey(row => new { row.CandidateId, row.StatementDateEvidenceExtractionId })
            .HasPrincipalKey(candidate => new { candidate.CandidateId, candidate.ExtractionId }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Speakers).WithOne().HasForeignKey(speaker => new { speaker.CandidateId, speaker.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.OtherSpeakers).WithOne().HasForeignKey(speaker => new { speaker.CandidateId, speaker.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Issues).WithOne().HasForeignKey(issue => new { issue.CandidateId, issue.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.PolicyAreas).WithOne().HasForeignKey(area => new { area.CandidateId, area.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Quotations).WithOne().HasForeignKey(quotation => new { quotation.CandidateId, quotation.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.BillNumbers).WithOne().HasForeignKey(bill => new { bill.CandidateId, bill.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
    }
}
