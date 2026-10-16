using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefRevisionRowConfiguration : IEntityTypeConfiguration<BriefRevisionRow>
{
    public void Configure(EntityTypeBuilder<BriefRevisionRow> builder)
    {
        builder.ToTable("brief_revisions", table =>
        {
            table.HasCheckConstraint("ck_brief_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_brief_revisions_author_kind", "author_kind IN ('Person','Analysis')");
            table.HasCheckConstraint("ck_brief_revisions_run", "(author_kind = 'Analysis') = (run_id IS NOT NULL)");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.AuthorKind).HasColumnName("author_kind").HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.RevisionJson).HasColumnName("revision_json").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.HasIndex(row => row.RunId).IsUnique();
        builder.HasOne<BriefDraftRow>().WithMany().HasForeignKey(row => row.RecordId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BriefAnalysisRunRow>().WithMany().HasForeignKey(row => row.RunId).OnDelete(DeleteBehavior.Restrict);
    }
}
