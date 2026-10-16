using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefScheduleRowConfiguration : IEntityTypeConfiguration<BriefScheduleRow>
{
    public void Configure(EntityTypeBuilder<BriefScheduleRow> builder)
    {
        builder.ToTable("brief_schedule", table =>
            table.HasCheckConstraint("ck_brief_schedule_week", "EXTRACT(ISODOW FROM first_week_start) = 1"));
        builder.HasKey(row => row.Task);
        builder.Property(row => row.Task).HasColumnName("task").HasMaxLength(32);
        builder.Property(row => row.FirstWeekStart).HasColumnName("first_week_start");
    }
}
