using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteAnalysisBudgetRowConfiguration : IEntityTypeConfiguration<VoteAnalysisBudgetRow>
{
    public void Configure(EntityTypeBuilder<VoteAnalysisBudgetRow> builder)
    {
        builder.ToTable("vote_analysis_budget", table =>
            table.HasCheckConstraint("ck_vote_analysis_budget_charge", "charged_tokens >= 0"));
        builder.HasKey(row => new { row.DayUtc, row.Task });
        builder.Property(row => row.DayUtc).HasColumnName("day_utc");
        builder.Property(row => row.Task).HasColumnName("task").HasMaxLength(32);
        builder.Property(row => row.ChargedTokens).HasColumnName("charged_tokens");
    }
}
