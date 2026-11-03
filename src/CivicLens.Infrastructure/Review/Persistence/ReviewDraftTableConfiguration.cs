using CivicLens.Core.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal static class ReviewDraftTableConfiguration
{
    /// <summary>The rows that need a person. An approved or a rejected record has left the queue.</summary>
    public const string NeedsAPersonFilter = "review_status <> 'Approved' AND review_status <> 'Rejected'";

    public static string StatusCheck(bool allowNotDrafted) => "review_status IN (" + string.Join(",",
        Enum.GetValues<ReviewStatus>().Where(status => allowNotDrafted || status != ReviewStatus.NotDrafted).Select(status => $"'{status}'")) + ")";

    public static void ConfigureLastRevised<TRow>(EntityTypeBuilder<TRow> builder, string table, string keyProperty)
        where TRow : class, IReviewDraftRow
    {
        builder.Property(row => row.LastRevisedAtUtcTicks).HasColumnName("last_revised_at_utc_ticks");
        builder.HasIndex(nameof(IReviewDraftRow.LastRevisedAtUtcTicks), keyProperty).IsDescending(true, true)
            .HasFilter(NeedsAPersonFilter).HasDatabaseName($"ix_{table}_needs_action_revised");
    }
}
