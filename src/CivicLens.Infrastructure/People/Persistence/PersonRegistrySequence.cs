using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.People.Persistence;

internal static class PersonRegistrySequence
{
    private const string Name = "person_registry_sequence";

    public static void Configure(ModelBuilder modelBuilder) => modelBuilder.HasSequence<long>(Name);

    public static PropertyBuilder<long> TakesNext(this PropertyBuilder<long> column) =>
        column.HasColumnName("registry_sequence").HasDefaultValueSql($"nextval('{Name}')").ValueGeneratedOnAdd();
}
