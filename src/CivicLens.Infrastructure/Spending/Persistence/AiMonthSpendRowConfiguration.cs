using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Spending.Persistence;

internal sealed class AiMonthSpendRowConfiguration : IEntityTypeConfiguration<AiMonthSpendRow>
{
    public void Configure(EntityTypeBuilder<AiMonthSpendRow> builder)
    {
        builder.ToTable("ai_month_spend", table =>
        {
            table.HasCheckConstraint("ck_ai_month_spend_month", "EXTRACT(DAY FROM month_start) = 1");
            table.HasCheckConstraint("ck_ai_month_spend_charge", "charged_micros >= 0");
        });
        builder.HasKey(row => row.MonthStart);
        builder.Property(row => row.MonthStart).HasColumnName("month_start").HasColumnType("date");
        builder.Property(row => row.ChargedMicros).HasColumnName("charged_micros");
    }
}
