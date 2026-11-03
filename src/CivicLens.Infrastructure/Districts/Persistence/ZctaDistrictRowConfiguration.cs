using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Roster.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Districts.Persistence;

internal sealed class ZctaDistrictRowConfiguration : IEntityTypeConfiguration<ZctaDistrictRow>
{
    public void Configure(EntityTypeBuilder<ZctaDistrictRow> builder)
    {
        builder.ToTable("zcta_districts", table =>
        {
            table.HasCheckConstraint("ck_zcta_districts_zcta", "zcta ~ '^[0-9]{5}$'");
            table.HasCheckConstraint("ck_zcta_districts_land", "district >= 0 AND land_area_square_meters > 0");
        });
        builder.HasKey(row => new { row.AttemptId, row.Zcta, row.State, row.District });
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").IsRequired();
        builder.Property(row => row.Zcta).HasColumnName("zcta").HasMaxLength(5).IsRequired();
        builder.Property(row => row.State).HasColumnName("state").HasMaxLength(RosterLimits.State).IsRequired();
        builder.Property(row => row.District).HasColumnName("district");
        builder.Property(row => row.LandAreaSquareMeters).HasColumnName("land_area_square_meters");
        builder.HasOne<RosterSnapshotRow>().WithMany().HasForeignKey(row => row.AttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
