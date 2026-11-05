using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.BillStream;

internal sealed class BillActionRowConfiguration : IEntityTypeConfiguration<BillActionRow>
{
    public void Configure(EntityTypeBuilder<BillActionRow> builder)
    {
        builder.ToTable("bill_actions", table =>
        {
            table.HasCheckConstraint("ck_bill_actions_position", "position >= 0");
            table.HasCheckConstraint("ck_bill_actions_text", "btrim(text) <> ''");
        });
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.JobId).HasColumnName("job_id").HasMaxLength(128);
        builder.Property(row => row.ActionDate).HasColumnName("action_date");
        builder.Property(row => row.ActionTime).HasColumnName("action_time");
        builder.Property(row => row.Text).HasColumnName("text");
        builder.Property(row => row.Type).HasColumnName("type");
        builder.Property(row => row.ActionCode).HasColumnName("action_code");
        builder.Property(row => row.SourceSystemCode).HasColumnName("source_system_code");
        builder.Property(row => row.SourceSystemName).HasColumnName("source_system_name");
        builder.HasKey(row => new { row.DetailJobId, row.Position });
        builder.HasOne<BillGateRow>().WithMany().HasForeignKey(row => row.DetailJobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BillPageRow>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Restrict);
    }
}
