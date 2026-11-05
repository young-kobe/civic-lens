using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Spending.Persistence;

internal sealed class AiMonthlyCapRevisionRowConfiguration : IEntityTypeConfiguration<AiMonthlyCapRevisionRow>
{
    public void Configure(EntityTypeBuilder<AiMonthlyCapRevisionRow> builder)
    {
        builder.ToTable("ai_monthly_cap_revisions", table =>
        {
            table.HasCheckConstraint("ck_ai_monthly_cap_revisions_number", "revision_number >= 1");
            table.HasCheckConstraint("ck_ai_monthly_cap_revisions_cap", "cap_micros >= 0");
        });
        builder.HasKey(row => row.RevisionNumber);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number").ValueGeneratedNever();
        builder.Property(row => row.CapMicros).HasColumnName("cap_micros");
        builder.Property(row => row.AuthorSubject).HasColumnName("author_subject").HasMaxLength(256).IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
    }
}
