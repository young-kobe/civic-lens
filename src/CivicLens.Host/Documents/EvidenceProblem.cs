using System.Text.RegularExpressions;
using CivicLens.Host.Components.Ui;

namespace CivicLens.Host.Documents;

public sealed partial record EvidenceProblem(int StatusCode, string Title, string Text, Tone Tone)
{
    public static EvidenceProblem Invalid { get; } = new(StatusCodes.Status400BadRequest, "This link is not valid",
        "Check the link, or open the evidence again from Review.", Tone.Bad);

    public static EvidenceProblem OutsideText { get; } = new(StatusCodes.Status400BadRequest, "This passage is not in the saved text",
        "The passage must fit inside the saved text and cannot split a character. Open the saved text and choose the passage again.", Tone.Bad);

    public static EvidenceProblem HistoryTooLarge { get; } = new(StatusCodes.Status200OK, "This history is too large to show",
        "It has more observations than this page can show safely, so none are listed.", Tone.Warn);

    public static EvidenceProblem Unavailable { get; } = new(StatusCodes.Status503ServiceUnavailable, "Evidence is unavailable",
        "The evidence store could not provide this record. Try again later.", Tone.Bad);

    public static EvidenceProblem Missing(string what) => new(StatusCodes.Status404NotFound, $"{what} not found",
        "Nothing is saved with this ID. Check the link.", Tone.Warn);

    public static void SetStatus(HttpResponse response, int statusCode) =>
        // Blazor discards the page body when a component sets 404 before rendering, so set it as the response starts.
        response.OnStarting(() =>
        {
            response.StatusCode = statusCode;
            return Task.CompletedTask;
        });

    public static bool IsHash(string? value) => value is not null && HashPattern().IsMatch(value);

    [GeneratedRegex("\\A[0-9a-f]{64}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
}
