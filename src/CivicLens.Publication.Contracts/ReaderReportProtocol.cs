namespace CivicLens.Publication.Contracts;

/// <summary>The wire rules of reader reports: the routes, the form fields, the caps, and the report categories. The search service and its puller share them.</summary>
public static class ReaderReportProtocol
{
    public const string SubmitPath = "/v1/reports";
    public const string PullPath = "/internal/v1/reports";
    public const string RecordIdField = "recordId";
    public const string RevisionField = "revision";
    public const string CategoryField = "category";
    public const string TextField = "text";
    public const string TurnstileField = "cf-turnstile-response";
    public const string AfterParameter = "after";
    public const string LimitParameter = "limit";
    public const string WrongQuoteCategory = "wrong-quote";
    public const string WrongDateCategory = "wrong-date";
    public const string WrongAttributionCategory = "wrong-attribution";
    public const string MissingContextCategory = "missing-context";
    public const string BrokenSourceLinkCategory = "broken-source-link";
    public const string OtherCategory = "other";
    public const int MaximumTextCharacters = 2000;
    public const int MaximumRequestBytes = 32 * 1024;
    public const int DefaultBatchSize = 100;
    public const int MaximumBatchSize = 200;
    public const int MaximumCursorLength = 100;
    public const int MinimumPullTokenCharacters = 32;
    public const int PurgeAfterDays = 7;
    public const int ClosedRetentionDays = 365;

    public static IReadOnlyList<string> Categories { get; } =
        [WrongQuoteCategory, WrongDateCategory, WrongAttributionCategory, MissingContextCategory, BrokenSourceLinkCategory, OtherCategory];

    public static bool IsPullToken(string? token) => token is not null && token.Length >= MinimumPullTokenCharacters;

    public static bool IsCategory(string? category) => category is not null && Categories.Contains(category);

    /// <summary>The text is optional, except for the category "other". It holds no NUL character, which Postgres cannot store.</summary>
    public static bool IsText(string category, string text) =>
        text.Length <= MaximumTextCharacters && !text.Contains('\0', StringComparison.Ordinal) &&
        (category != OtherCategory || !string.IsNullOrWhiteSpace(text));
}
