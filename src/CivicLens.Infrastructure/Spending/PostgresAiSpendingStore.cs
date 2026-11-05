using CivicLens.Application.Spending;
using CivicLens.Core.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Spending.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Spending;

public sealed class PostgresAiSpendingStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IAiSpendingStore
{
    public async Task<AiSpendingView> GetAsync(int changeLimit, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var month = AiSpendingPolicy.MonthOf((await DatabaseClock.ReadAsync(db, cancellationToken)).Day);
        var charged = await db.Set<AiMonthSpendRow>().AsNoTracking().Where(row => row.MonthStart == month)
            .Select(row => row.ChargedMicros).SingleOrDefaultAsync(cancellationToken);
        var caps = await db.Set<AiMonthlyCapRevisionRow>().AsNoTracking().OrderByDescending(row => row.RevisionNumber).Take(changeLimit)
            .ToArrayAsync(cancellationToken);
        var prices = db.Set<AiModelPriceRevisionRow>().AsNoTracking();
        var current = await prices.Where(row => !prices.Any(newer => newer.Model == row.Model && newer.RevisionNumber > row.RevisionNumber))
            .OrderBy(row => row.Model).ToArrayAsync(cancellationToken);
        var changes = await prices.OrderByDescending(row => row.CreatedAtUtcTicks).ThenBy(row => row.Model).Take(changeLimit)
            .ToArrayAsync(cancellationToken);
        return new(month, charged, [.. caps.Select(ToRevision)], [.. current.Select(ToRevision)], [.. changes.Select(ToRevision)]);
    }

    public async Task<ModelPrice?> GetPriceAsync(string model, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await AiSpendLedger.NewestPriceAsync(db, model, cancellationToken);
    }

    public async Task AddChargeAsync(string model, AnalysisTokenUsage usage, long chargedTokens, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AiSpendLedger.ChargeAsync(db, (await DatabaseClock.ReadAsync(db, cancellationToken)).Day, model, usage, chargedTokens, 0, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveCapAsync(string authorSubject, long capMicros, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.AiSpending, cancellationToken);
        var newest = await db.Set<AiMonthlyCapRevisionRow>().MaxAsync(row => (int?)row.RevisionNumber, cancellationToken);
        db.Add(new AiMonthlyCapRevisionRow
        {
            RevisionNumber = (newest ?? 0) + 1,
            CapMicros = capMicros,
            AuthorSubject = authorSubject,
            CreatedAtUtcTicks = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SavePriceAsync(string authorSubject, ModelPrice price, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await PostgresAdvisoryLock.BeginWriteAsync(db, PostgresAdvisoryLock.AiSpending, cancellationToken);
        var newest = await db.Set<AiModelPriceRevisionRow>().Where(row => row.Model == price.Model)
            .MaxAsync(row => (int?)row.RevisionNumber, cancellationToken);
        db.Add(new AiModelPriceRevisionRow
        {
            Model = price.Model,
            RevisionNumber = (newest ?? 0) + 1,
            InputMicros = price.InputMicros,
            OutputMicros = price.OutputMicros,
            CacheReadMicros = price.CacheReadMicros,
            CacheWriteMicros = price.CacheWriteMicros,
            AuthorSubject = authorSubject,
            CreatedAtUtcTicks = (await DatabaseClock.ReadAsync(db, cancellationToken)).Ticks
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static MonthlyCapRevision ToRevision(AiMonthlyCapRevisionRow row) =>
        new(row.RevisionNumber, row.CapMicros, row.AuthorSubject, RecordedAt(row.CreatedAtUtcTicks));

    private static ModelPriceRevision ToRevision(AiModelPriceRevisionRow row) =>
        new(row.RevisionNumber, row.ToPrice(), row.AuthorSubject, RecordedAt(row.CreatedAtUtcTicks));

    private static DateTimeOffset RecordedAt(long ticks) => new(ticks, TimeSpan.Zero);
}
