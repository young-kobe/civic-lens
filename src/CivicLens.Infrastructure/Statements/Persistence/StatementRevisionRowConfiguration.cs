using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionRowConfiguration : IEntityTypeConfiguration<StatementRevisionRow>
{
    public void Configure(EntityTypeBuilder<StatementRevisionRow> builder)
    {
        builder.ToTable("statement_revisions", table =>
        {
            table.HasCheckConstraint("ck_statement_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_statement_revisions_author_kind", "author_kind IN ('Person','Analysis')");
            table.HasCheckConstraint("ck_statement_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
        });
        builder.HasKey(row => new { row.CandidateId, row.RevisionNumber });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.HasIndex(row => row.RunId).IsUnique();
        builder.HasOne<StatementDraftRow>().WithMany().HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StatementAnalysisRunRow>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}
