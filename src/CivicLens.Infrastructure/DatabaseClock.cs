using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure;

internal readonly record struct DatabaseTime(long Ticks)
{
    public DateOnly Day => DateOnly.FromDateTime(new DateTime(Ticks, DateTimeKind.Utc));
    public DateTimeOffset Offset => new(Ticks, TimeSpan.Zero);
}

internal static class DatabaseClock
{
    public const string TicksSql =
        "(EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint";

    public static async Task<DatabaseTime> ReadAsync(DbContext db, CancellationToken cancellationToken) =>
        new(await db.Database.SqlQueryRaw<long>("SELECT " + TicksSql + " AS \"Value\"").SingleAsync(cancellationToken));
}
