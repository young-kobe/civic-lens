using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Publication.Persistence;

internal sealed class PublishedBillHistoryActionRowConfiguration : IEntityTypeConfiguration<PublishedBillHistoryActionRow>
{
    public void Configure(EntityTypeBuilder<PublishedBillHistoryActionRow> builder)
    {
        builder.ToTable("published_bill_history_actions", table =>
        {
            table.HasCheckConstraint("ck_published_bill_history_actions_position", "position >= 0");
            table.HasCheckConstraint("ck_published_bill_history_actions_projection_version", "projection_version >= 0");
            table.HasCheckConstraint("ck_published_bill_history_actions_text", "text ~ '\\S'");
        });
        builder.Property(row => row.DetailJobId).HasColumnName("detail_job_id").HasMaxLength(128);
        builder.Property(row => row.ProjectionVersion).HasColumnName("projection_version").HasDefaultValue(0);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.ActionDate).HasColumnName("action_date");
        builder.Property(row => row.ActionTime).HasColumnName("action_time");
        builder.Property(row => row.Text).HasColumnName("text");
        builder.Property(row => row.Type).HasColumnName("type");
        builder.Property(row => row.ActionCode).HasColumnName("action_code");
        builder.Property(row => row.SourceSystemCode).HasColumnName("source_system_code");
        builder.Property(row => row.SourceSystemName).HasColumnName("source_system_name");
        builder.HasKey(row => new { row.DetailJobId, row.ProjectionVersion, row.Position });
        builder.HasOne<PublishedBillHistoryRow>().WithMany().HasForeignKey(row => new { row.DetailJobId, row.ProjectionVersion }).OnDelete(DeleteBehavior.Restrict);
    }
}
