using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionAnalysisBudgetRowConfiguration : IEntityTypeConfiguration<ActionAnalysisBudgetRow>
{
    public void Configure(EntityTypeBuilder<ActionAnalysisBudgetRow> builder)
    {
        builder.ToTable("action_analysis_budget", table =>
            table.HasCheckConstraint("ck_action_analysis_budget_charge", "charged_tokens >= 0"));
        builder.HasKey(row => new { row.DayUtc, row.Task });
        builder.Property(row => row.DayUtc).HasColumnName("day_utc");
        builder.Property(row => row.Task).HasColumnName("task").HasMaxLength(32);
        builder.Property(row => row.ChargedTokens).HasColumnName("charged_tokens");
    }
}
