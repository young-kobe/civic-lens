using CivicLens.Application.Analysis;
using CivicLens.Application.Spending;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Spending.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Spending;

internal readonly record struct SpendReservation(long Micros, string? WaitCode);

/// <summary>Keeps the dollar total of a UTC month. The queue calls it inside the transaction that reserves or settles tokens.</summary>
internal static class AiSpendLedger
{
    private const string EnsureMonthSql = "INSERT INTO ai_month_spend (month_start, charged_micros) VALUES ({0}, 0) ON CONFLICT DO NOTHING";
    private const string LockMonthSql = "SELECT * FROM ai_month_spend WHERE month_start = {0} FOR UPDATE";

    public static async Task<SpendReservation> ReserveAsync(CollectionAttemptDbContext db, string model, long tokens, DateOnly day,
        CancellationToken cancellationToken)
    {
        var cap = await db.Set<AiMonthlyCapRevisionRow>().AsNoTracking().OrderByDescending(row => row.RevisionNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (cap is null) return new(0, AnalysisErrorCodes.MonthlyCapNotSet);
        if (cap.CapMicros == 0) return new(0, AnalysisErrorCodes.MonthlyDollarCap);
        var price = await NewestPriceAsync(db, model, cancellationToken);
        if (price is null) return new(0, AnalysisErrorCodes.ModelPriceMissing);
        var worstCase = price.WorstCaseMicros(tokens);
        var month = await LockMonthAsync(db, day, cancellationToken);
        if (month.ChargedMicros + worstCase > cap.CapMicros) return new(0, AnalysisErrorCodes.MonthlyDollarCap);
        month.ChargedMicros += worstCase;
        return new(worstCase, null);
    }

    /// <summary>Takes the month row lock. Call it as the last statement before the commit, so no transaction waits on another lock while it holds the month row.</summary>
    public static async Task SettleAsync(CollectionAttemptDbContext db, DateOnly budgetDay, AnalysisRun run, long reservedMicros,
        CancellationToken cancellationToken)
    {
        var price = await NewestPriceAsync(db, run.Model, cancellationToken)
            ?? throw new InvalidOperationException("A run that settles a reservation needs the price of its model.");
        await AddAsync(db, budgetDay, price.CostMicros(run.Usage, run.ChargedTokens) - reservedMicros, cancellationToken);
    }

    public static async Task AddAsync(CollectionAttemptDbContext db, DateOnly day, long micros, CancellationToken cancellationToken) =>
        (await LockMonthAsync(db, day, cancellationToken)).ChargedMicros += micros;

    public static async Task<ModelPrice?> NewestPriceAsync(CollectionAttemptDbContext db, string model, CancellationToken cancellationToken) =>
        (await db.Set<AiModelPriceRevisionRow>().AsNoTracking().Where(row => row.Model == model)
            .OrderByDescending(row => row.RevisionNumber).FirstOrDefaultAsync(cancellationToken))?.ToPrice();

    private static async Task<AiMonthSpendRow> LockMonthAsync(CollectionAttemptDbContext db, DateOnly day, CancellationToken cancellationToken)
    {
        var month = AiSpendingPolicy.MonthOf(day);
        _ = await db.Database.ExecuteSqlRawAsync(EnsureMonthSql, [month], cancellationToken);
        return await db.Set<AiMonthSpendRow>().FromSqlRaw(LockMonthSql, month).SingleAsync(cancellationToken);
    }
}
