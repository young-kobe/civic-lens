using System.Text.Json.Serialization;
using CivicLens.Core.Review;

namespace CivicLens.Core.Votes;

/// <summary>
/// The short plain text that says what a vote decides. A model writes it from the Congressional Research Service summary of the voted bill, and code
/// checks it before the revision exists. The quote is the exact span of that summary that backs the text. The source hash names the bill line and the
/// summary that the model read, so a change of either one drafts the text again. The origin is always Model and the label is fixed, so a public
/// reader always sees the label beside the text.
/// </summary>
public sealed record VoteDecides(string Text, string Quote, string SourceHash)
{
    public const int MaximumTextLength = 600;
    public const int MaximumQuoteLength = 300;

    /// <summary>Provisional public wording, the one of the bill line precedent. The owner has not chosen the final text.</summary>
    public const string Label = "AI summary of the Congressional Research Service summary.";

    [JsonIgnore]
    public ReviewTextOrigin TextOrigin => ReviewTextOrigin.Model;

    public void ValidateShape()
    {
        ReviewTextRules.ValidateText(Text, MaximumTextLength, nameof(Text));
        ReviewTextRules.ValidateText(Quote, MaximumQuoteLength, nameof(Quote));
        if (!LowercaseHex.Is(SourceHash, 64)) throw new ArgumentException("The source hash of what a vote decides must be 64 lowercase hex characters.");
    }
}
