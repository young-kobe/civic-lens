using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure;

internal readonly record struct DatabaseTime(long Ticks)
{
    public DateOnly Day => DateOnly.FromDateTime(new DateTime(Ticks, DateTimeKind.Utc));
    public DateTimeOffset Offset => new(Ticks, TimeSpan.Zero);
}

internal static class DatabaseClock
{
    private const string EpochToTicksSql = "::numeric * 10000000 + 621355968000000000)::bigint";

    public const string TicksSql = "(EXTRACT(EPOCH FROM clock_timestamp())" + EpochToTicksSql;

    public static string TicksOfSql(string instant) => $"(EXTRACT(EPOCH FROM {instant}){EpochToTicksSql}";

    public static async Task<DatabaseTime> ReadAsync(DbContext db, CancellationToken cancellationToken) =>
        new(await db.Database.SqlQueryRaw<long>("SELECT " + TicksSql + " AS \"Value\"").SingleAsync(cancellationToken));
}
