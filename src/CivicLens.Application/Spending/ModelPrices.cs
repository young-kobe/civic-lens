namespace CivicLens.Application.Spending;

/// <summary>The Anthropic price list for each model that a stage uses, checked 2026-11-05 at platform.claude.com/docs/en/about-claude/pricing.
/// The cache write price is the 5 minute price, because the calls ask for a 5 minute cache. A price change is a code change.</summary>
public static class ModelPrices
{
    private const long HaikuTierPromptTokens = 100_000;

    private static readonly IReadOnlyDictionary<string, ModelPrice> ByModel = new[]
    {
        new ModelPrice("claude-sonnet-5-5", [new(null, 2_000_000, 10_000_000, 100_000, 2_500_000)]),
        new ModelPrice("claude-haiku-5-5",
        [
            new(HaikuTierPromptTokens, 100_000, 500_000, 10_000, 125_000),
            new(null, 500_000, 2_500_000, 50_000, 625_000)
        ])
    }.ToDictionary(price => price.Model);

    public static ModelPrice For(string model) =>
        ByModel.TryGetValue(model, out var price) ? price : throw new InvalidOperationException($"The model {model} has no price in ModelPrices.");
}
