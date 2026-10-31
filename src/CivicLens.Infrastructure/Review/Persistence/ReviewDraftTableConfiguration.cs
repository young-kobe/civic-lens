using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal static class ReviewDraftTableConfiguration
{
    public static void ConfigureLastRevised<TRow>(EntityTypeBuilder<TRow> builder, string table, string keyProperty)
        where TRow : class, IReviewDraftRow
    {
        builder.Property(row => row.LastRevisedAtUtcTicks).HasColumnName("last_revised_at_utc_ticks");
        builder.HasIndex(nameof(IReviewDraftRow.LastRevisedAtUtcTicks), keyProperty).IsDescending(true, true)
            .HasFilter("review_status <> 'Approved'").HasDatabaseName($"ix_{table}_needs_action_revised");
    }
}
