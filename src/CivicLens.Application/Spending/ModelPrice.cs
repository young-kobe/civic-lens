using CivicLens.Core.Analysis;

namespace CivicLens.Application.Spending;

/// <summary>The price of one model. Each price is in micro-dollars for one million tokens.</summary>
public sealed record ModelPrice(string Model, long InputMicros, long OutputMicros, long CacheReadMicros, long CacheWriteMicros)
{
    public const long MaximumMicros = 1_000_000_000;
    private const long TokensPerPrice = 1_000_000;

    private long HighestMicros => Math.Max(Math.Max(InputMicros, OutputMicros), Math.Max(CacheReadMicros, CacheWriteMicros));

    public long WorstCaseMicros(long tokens) => Ceiling(tokens * HighestMicros);

    /// <summary>Prices the used tokens at their own price and the rest of the charged tokens at the highest price.</summary>
    public long CostMicros(AnalysisTokenUsage usage, long chargedTokens) =>
        Ceiling(usage.InputTokens * InputMicros + usage.OutputTokens * OutputMicros + usage.CacheReadTokens * CacheReadMicros +
            usage.CacheWriteTokens * CacheWriteMicros + (chargedTokens - usage.Total) * HighestMicros);

    private static long Ceiling(long scaled) => (scaled + TokensPerPrice - 1) / TokensPerPrice;
}
