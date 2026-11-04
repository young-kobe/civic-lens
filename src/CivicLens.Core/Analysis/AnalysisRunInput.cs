using System.Security.Cryptography;
using System.Text;

namespace CivicLens.Core.Analysis;

/// <summary>The exact text and settings that one model call received.</summary>
public sealed record AnalysisRunInput(string SystemPrompt, string UserMessage, string OutputSchema, string Effort, int MaximumOutputTokens)
{
    public const int MaximumPromptBytes = 100_000;
    public const int MaximumEffortLength = 16;

    public string SystemPromptSha256 { get; } = TextHash(SystemPrompt);
    public string OutputSchemaSha256 { get; } = TextHash(OutputSchema);

    public static string TextHash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    public void Validate()
    {
        if (!IsBoundedText(SystemPrompt) || !IsBoundedText(UserMessage) || !IsBoundedText(OutputSchema))
            throw new ArgumentException($"Analysis run input text holds 1 to {MaximumPromptBytes} characters and no NUL character.");
        if (string.IsNullOrWhiteSpace(Effort) || Effort.Length > MaximumEffortLength || Effort.Contains('\0') || MaximumOutputTokens <= 0)
            throw new ArgumentException("Analysis run input settings are invalid.");
    }

    private static bool IsBoundedText(string? text) => text is { Length: > 0 and <= MaximumPromptBytes } && !text.Contains('\0');
}
