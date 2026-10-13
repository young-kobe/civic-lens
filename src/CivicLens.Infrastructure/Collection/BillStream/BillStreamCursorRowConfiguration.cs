using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillStreamCursorRowConfiguration : IEntityTypeConfiguration<BillStreamCursorRow>
{
    public void Configure(EntityTypeBuilder<BillStreamCursorRow> builder)
    {
        builder.ToTable("bill_stream_cursors", table =>
        {
            table.HasCheckConstraint("ck_bill_stream_cursors_updated", "updated_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
            table.HasCheckConstraint("ck_bill_stream_cursors_pass_admitted_bills", "pass_admitted_bills >= 0");
        });
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.LastProcessedDay).HasColumnName("last_processed_day");
        builder.Property(row => row.PassAdmittedBills).HasColumnName("pass_admitted_bills");
        builder.Property(row => row.UpdatedAtUtcTicks).HasColumnName("updated_at_utc_ticks");
        builder.HasKey(row => row.SourceId);
    }
}
