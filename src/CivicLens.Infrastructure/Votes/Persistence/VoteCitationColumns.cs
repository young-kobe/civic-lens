using CivicLens.Infrastructure.Documents.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

/// <summary>Owns the three columns, the extraction key, and the bounds check of one source span.
/// An optional span has all three columns null or all three set.</summary>
internal static class VoteCitationColumns
{
    public static void Configure<TRow>(EntityTypeBuilder<TRow> builder, string table, string property, string column, bool optional)
        where TRow : class
    {
        builder.Property(property + "ExtractionId").HasColumnName($"{column}_extraction_id").HasMaxLength(64);
        builder.Property(property + "Start").HasColumnName($"{column}_start");
        builder.Property(property + "Length").HasColumnName($"{column}_length");
        builder.HasOne<DocumentExtractionRow>().WithMany().HasForeignKey(property + "ExtractionId").OnDelete(DeleteBehavior.Restrict);
        var bounds = $"{column}_start >= 0 AND {column}_length > 0";
        var span = optional
            ? $"({column}_extraction_id IS NULL) = ({column}_start IS NULL) AND ({column}_extraction_id IS NULL) = ({column}_length IS NULL) AND {bounds}"
            : bounds;
        builder.ToTable(table, constraints => constraints.HasCheckConstraint($"ck_{table}_{column}", span));
    }
}
