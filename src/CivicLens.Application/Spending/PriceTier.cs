namespace CivicLens.Application.Spending;

/// <summary>The prices of the calls whose prompt has at most <paramref name="PromptTokensUpTo"/> tokens. The last tier of a model has no limit.
/// Each price is in micro-dollars for one million tokens.</summary>
public sealed record PriceTier(long? PromptTokensUpTo, long InputMicros, long OutputMicros, long CacheReadMicros, long CacheWriteMicros)
{
    public long HighestMicros => Math.Max(Math.Max(InputMicros, OutputMicros), Math.Max(CacheReadMicros, CacheWriteMicros));
}
