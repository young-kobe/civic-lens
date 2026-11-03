using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

/// <summary>Owns the headline and summary columns of a revision table, and their length checks. A kind whose draft revision starts
/// empty allows an empty headline and summary. Then the column type alone bounds the headline. The row type has the properties of <see cref="IHeadlineSummaryRow"/>.</summary>
internal static class HeadlineSummaryColumns
{
    public static void Configure<TRow>(EntityTypeBuilder<TRow> builder, string table, int maximumHeadlineLength, int maximumSummaryLength,
        bool allowEmpty)
        where TRow : class, IHeadlineSummaryRow
    {
        builder.ToTable(table, constraints =>
        {
            if (!allowEmpty) constraints.HasCheckConstraint($"ck_{table}_headline", $"length(btrim(headline)) BETWEEN 1 AND {maximumHeadlineLength}");
            constraints.HasCheckConstraint($"ck_{table}_summary", allowEmpty
                ? $"length(summary) <= {maximumSummaryLength}" : $"length(summary) BETWEEN 1 AND {maximumSummaryLength}");
        });
        builder.Property(row => row.Headline).HasColumnName("headline").HasMaxLength(maximumHeadlineLength);
        builder.Property(row => row.Summary).HasColumnName("summary");
    }
}
