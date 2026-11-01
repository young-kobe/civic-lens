using CivicLens.Application.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Reports.Persistence;

internal sealed class ReaderReportRowConfiguration : IEntityTypeConfiguration<ReaderReportRow>
{
    public void Configure(EntityTypeBuilder<ReaderReportRow> builder)
    {
        builder.ToTable("reader_reports", table =>
        {
            table.HasCheckConstraint("ck_reader_reports_record_id", "record_id ~ '^([0-9a-f]{32}|[0-9a-f]{64})$'");
            table.HasCheckConstraint("ck_reader_reports_revision", "revision_number >= 1");
            table.HasCheckConstraint("ck_reader_reports_status", "status IN ('Open','Dismissed','CorrectionNeeded')");
            table.HasCheckConstraint("ck_reader_reports_resolution",
                "(status = 'Open') = (resolved_by IS NULL AND resolved_at_utc_ticks IS NULL) AND (status <> 'CorrectionNeeded' OR note IS NOT NULL)");
            table.HasCheckConstraint("ck_reader_reports_version", "version >= 0");
        });
        builder.HasKey(row => row.ReportId);
        builder.Property(row => row.ReportId).HasColumnName("report_id");
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.Category).HasColumnName("category").HasMaxLength(32).IsRequired();
        builder.Property(row => row.Text).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(row => row.ReceivedAtUtcTicks).HasColumnName("received_at_utc_ticks");
        builder.Property(row => row.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.Note).HasColumnName("note").HasMaxLength(2000);
        builder.Property(row => row.ResolvedBy).HasColumnName("resolved_by").HasMaxLength(256);
        builder.Property(row => row.ResolvedAtUtcTicks).HasColumnName("resolved_at_utc_ticks");
        builder.Property(row => row.Version).HasColumnName("version");
        builder.HasIndex(row => new { row.Status, row.ReceivedAtUtcTicks }).HasDatabaseName("ix_reader_reports_status_received");
        builder.HasIndex(row => row.RecordId).HasDatabaseName("ix_reader_reports_record");
    }
}
