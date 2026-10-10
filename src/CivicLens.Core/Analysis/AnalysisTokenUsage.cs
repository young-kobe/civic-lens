namespace CivicLens.Core.Analysis;

public sealed record AnalysisTokenUsage(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheWriteTokens)
{
    public static readonly AnalysisTokenUsage None = new(0, 0, 0, 0);

    public long Total => InputTokens + OutputTokens + CacheReadTokens + CacheWriteTokens;

    public bool IsValid => InputTokens >= 0 && OutputTokens >= 0 && CacheReadTokens >= 0 && CacheWriteTokens >= 0;
}
