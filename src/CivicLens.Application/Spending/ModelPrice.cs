using CivicLens.Core.Analysis;

namespace CivicLens.Application.Spending;

/// <summary>The price of one model, in tiers by prompt size. A call pays the prices of one tier for all of its tokens.</summary>
public sealed record ModelPrice(string Model, IReadOnlyList<PriceTier> Tiers)
{
    private const long TokensPerPrice = 1_000_000;

    private long HighestMicros => Tiers.Max(tier => tier.HighestMicros);

    public long WorstCaseMicros(long tokens) => Ceiling(checked(tokens * HighestMicros));

    /// <summary>Prices each call at the tier of its prompt. It prices the rest of the charged tokens at the highest price of any tier.</summary>
    public long CostMicros(IReadOnlyCollection<AnalysisTokenUsage> calls, long chargedTokens) =>
        Ceiling(checked(calls.Sum(CallScaledMicros) + (chargedTokens - calls.Sum(call => call.Total)) * HighestMicros));

    private long CallScaledMicros(AnalysisTokenUsage usage)
    {
        var tier = TierOf(usage.InputTokens + usage.CacheReadTokens + usage.CacheWriteTokens);
        return usage.InputTokens * tier.InputMicros + usage.OutputTokens * tier.OutputMicros + usage.CacheReadTokens * tier.CacheReadMicros +
            usage.CacheWriteTokens * tier.CacheWriteMicros;
    }

    private PriceTier TierOf(long promptTokens) => Tiers.First(tier => tier.PromptTokensUpTo is null || promptTokens <= tier.PromptTokensUpTo);

    private static long Ceiling(long scaled) => checked(scaled + TokensPerPrice - 1) / TokensPerPrice;
}
