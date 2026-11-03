using CivicLens.Core.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

/// <summary>Owns the headline, summary, origin, and suggestion columns of a revision table, and their checks.
/// The row type has the properties of <see cref="IReviewTextRow"/>.</summary>
internal static class ReviewTextColumns
{
    public static void Configure<TRow>(EntityTypeBuilder<TRow> builder, string table, int maximumHeadlineLength, int maximumSummaryLength)
        where TRow : class, IReviewTextRow
    {
        builder.ToTable(table, constraints =>
        {
            constraints.HasCheckConstraint($"ck_{table}_headline", $"length(btrim(headline)) BETWEEN 1 AND {maximumHeadlineLength}");
            constraints.HasCheckConstraint($"ck_{table}_summary", $"length(summary) BETWEEN 1 AND {maximumSummaryLength}");
            constraints.HasCheckConstraint($"ck_{table}_text_origin", InList("text_origin", Enum.GetNames<ReviewTextOrigin>()));
            constraints.HasCheckConstraint($"ck_{table}_suggestion",
                "(suggestion_headline IS NULL) = (suggestion_summary IS NULL) AND (suggestion_headline IS NULL) = (suggestion_run_id IS NULL) AND " +
                "(suggestion_headline IS NULL) = (suggestion_fingerprint IS NULL)");
        });
        builder.Property(row => row.Headline).HasColumnName("headline").HasMaxLength(maximumHeadlineLength);
        builder.Property(row => row.Summary).HasColumnName("summary");
        builder.Property(row => row.TextOrigin).HasColumnName("text_origin").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.SuggestionHeadline).HasColumnName("suggestion_headline").HasMaxLength(maximumHeadlineLength);
        builder.Property(row => row.SuggestionSummary).HasColumnName("suggestion_summary");
        builder.Property(row => row.SuggestionRunId).HasColumnName("suggestion_run_id").HasMaxLength(32);
        builder.Property(row => row.SuggestionFingerprint).HasColumnName("suggestion_fingerprint").HasMaxLength(64);
    }

    public static string InList(string column, IEnumerable<string> names) => $"{column} IN ({string.Join(", ", names.Select(name => $"'{name}'"))})";
}
