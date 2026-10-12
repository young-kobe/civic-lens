using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CivicLens.Core.Analysis;

namespace CivicLens.Application.Analysis;

public static class AnalysisAnswerRules
{
    private static readonly string[] DateFormats =
        ["yyyy-MM-dd", "MMMM d, yyyy", "MMM d, yyyy", "MMM. d, yyyy", "d MMMM yyyy", "M/d/yyyy", "MM/dd/yyyy"];

    public static readonly JsonSerializerOptions OutputJson = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public static void CheckConfidence(double value, string name, List<string> errors)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1) errors.Add($"{name} must be a number from 0 to 1.");
    }

    public static void AddCitationError(string message, List<string> errors, List<string> citationErrors)
    {
        errors.Add(message);
        citationErrors.Add(message);
    }

    public static void CheckText(string? value, int maximumLength, string name, bool required, List<string> errors)
    {
        if (value is null && !required) return;
        if (string.IsNullOrWhiteSpace(value) && required) errors.Add($"{name} is required.");
        else if (value is not null && (value.Length > maximumLength || value.Contains('\0')))
            errors.Add($"{name} must be at most {maximumLength} characters with no NUL character.");
    }

    public static bool StatesDate(string quote, DateOnly date) =>
        DateFormats.Any(format => Regex.IsMatch(quote, $@"(?<!\d){Regex.Escape(date.ToString(format, CultureInfo.InvariantCulture))}(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    public static string Shorten(string? value) => Truncate(value ?? "", 80);

    public static ImmutableArray<string> Bound(IEnumerable<string> errors) =>
        [.. errors.Take(AnalysisRun.MaximumValidationErrors).Select(error => Truncate(error, AnalysisRun.MaximumValidationErrorLength))];

    private static string Truncate(string value, int maximumLength)
    {
        if (value.Length <= maximumLength) return value;
        var length = maximumLength - 3;
        if (char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length] + "...";
    }
}
