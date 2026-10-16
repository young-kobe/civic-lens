using CivicLens.Application.Review;

namespace CivicLens.Host.Review;

internal static class SuggestionActions
{
    public const string Use = "use";
    public const string Dismiss = "dismiss";

    public static SuggestionChoice ChoiceOf(string? action) => action switch
    {
        Use => SuggestionChoice.Use,
        Dismiss => SuggestionChoice.Dismiss,
        _ => SuggestionChoice.Keep
    };
}
