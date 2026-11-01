using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CivicLens.Infrastructure;

/// <summary>Owns the range check of every column that stores a UTC time as .NET ticks.</summary>
internal static class TimestampTickChecks
{
    private const string ColumnSuffix = "_utc_ticks";

    public static void Apply(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (StoreObjectIdentifier.Create(entityType, StoreObjectType.Table) is not { } table) continue;
            foreach (var column in entityType.GetProperties().Select(property => property.GetColumnName(table))
                         .Where(name => name is not null && name.EndsWith(ColumnSuffix, StringComparison.Ordinal)))
                entityType.AddCheckConstraint($"ck_{table.Name}_{column}", $"{column} BETWEEN 0 AND {DateTime.MaxValue.Ticks}");
        }
    }
}
