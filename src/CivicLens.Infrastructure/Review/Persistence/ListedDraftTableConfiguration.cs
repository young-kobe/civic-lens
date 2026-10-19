using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal static class ListedDraftTableConfiguration
{
    public static void ConfigureListing<TRow>(EntityTypeBuilder<TRow> builder, string table)
        where TRow : class, IListedDraftRow
    {
        builder.Property<DateOnly>(nameof(IListedDraftRow.EventOn)).HasColumnName("event_on").HasColumnType("date")
            .HasDefaultValue(new DateOnly(1, 1, 1));
        builder.Property<string[]>(nameof(IListedDraftRow.IssueIds)).HasColumnName("issue_ids").HasColumnType("text[]")
            .HasDefaultValueSql("'{}'");
        builder.Property<string[]>(nameof(IListedDraftRow.OfficialIds)).HasColumnName("official_ids").HasColumnType("text[]")
            .HasDefaultValueSql("'{}'");
        builder.HasIndex(nameof(IListedDraftRow.IssueIds)).HasMethod("gin").HasDatabaseName($"ix_{table}_issue_ids");
        builder.HasIndex(nameof(IListedDraftRow.OfficialIds)).HasMethod("gin").HasDatabaseName($"ix_{table}_official_ids");
    }
}
