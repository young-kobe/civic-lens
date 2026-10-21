using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Reports.Persistence;

internal sealed class ReaderReportCursorRowConfiguration : IEntityTypeConfiguration<ReaderReportCursorRow>
{
    public void Configure(EntityTypeBuilder<ReaderReportCursorRow> builder)
    {
        builder.ToTable("reader_report_cursor", table =>
            table.HasCheckConstraint("ck_reader_report_cursor_id", $"id = {ReaderReportCursorRow.OnlyId}"));
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(row => row.Cursor).HasColumnName("cursor").HasMaxLength(100).IsRequired();
    }
}
