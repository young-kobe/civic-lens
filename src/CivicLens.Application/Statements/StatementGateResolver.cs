using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;

namespace CivicLens.Application.Statements;

public static class StatementGateResolver
{
    private static readonly JsonSerializerOptions OutputJson = new(JsonSerializerDefaults.Web)
    {
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public static StatementGateResolution Resolve(string outputJson, StatementGateEvidence evidence, StatementGateRubric rubric)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(rubric);
        var output = Parse(outputJson);
        if (output is null) return Rejected(["The output is not valid JSON for the gate schema."], citationErrors: false);
        var errors = new List<string>();
        CheckConfidence(output.Confidence, "confidence", errors);
        CheckPolicyAreas(output.PolicyAreas, rubric, errors);
        var fieldErrors = errors.Count;
        var quotes = ResolveIssues(output.TrackedIssues, evidence, rubric, errors);
        if (errors.Count > 0) return Rejected(errors, citationErrors: errors.Count > fieldErrors);
        return new(quotes.IsEmpty ? StatementGateReasons.NoTrackedIssue : StatementGateReasons.Passed, quotes, [], false);
    }

    private static StatementGateOutput? Parse(string outputJson)
    {
        try
        {
            var output = JsonSerializer.Deserialize<StatementGateOutput>(outputJson, OutputJson);
            if (output is null || output.TrackedIssues.IsDefault || output.PolicyAreas.IsDefault) return null;
            return output.TrackedIssues.Any(item => item is null) || output.PolicyAreas.Any(item => item is null) ? null : output;
        }
        catch (JsonException) { return null; }
    }

    private static void CheckPolicyAreas(ImmutableArray<StatementGateOutputPolicyArea> areas, StatementGateRubric rubric, List<string> errors)
    {
        var listed = rubric.PolicyAreas.Select(area => area.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var area in areas)
        {
            CheckConfidence(area.Confidence, $"policyAreas confidence for \"{Shorten(area.Id)}\"", errors);
            if (area.Id is null || !listed.Contains(area.Id)) errors.Add($"policyAreas holds \"{Shorten(area.Id)}\", which is not a listed ID.");
            else if (!seen.Add(area.Id)) errors.Add($"policyAreas repeats \"{area.Id}\".");
        }
    }

    private static ImmutableArray<StatementGateQuote> ResolveIssues(ImmutableArray<StatementGateOutputIssue> issues,
        StatementGateEvidence evidence, StatementGateRubric rubric, List<string> errors)
    {
        var listed = rubric.Issues.Select(issue => issue.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var quotes = ImmutableArray.CreateBuilder<StatementGateQuote>(issues.Length);
        foreach (var issue in issues)
        {
            CheckConfidence(issue.Confidence, $"trackedIssues confidence for \"{Shorten(issue.Id)}\"", errors);
            if (issue.Id is null || !listed.Contains(issue.Id))
                errors.Add($"trackedIssues holds \"{Shorten(issue.Id)}\", which is not a listed ID.");
            else if (!seen.Add(issue.Id)) errors.Add($"trackedIssues repeats \"{issue.Id}\".");
            else if (Locate(issue, evidence.Extraction, errors) is { } quote) quotes.Add(quote);
        }
        return quotes.ToImmutable();
    }

    private static StatementGateQuote? Locate(StatementGateOutputIssue issue, DocumentExtraction extraction, List<string> errors)
    {
        if (string.IsNullOrEmpty(issue.Quote) || issue.Quote.Length > StatementGateTask.MaximumQuoteLength)
        {
            errors.Add($"The quote for \"{issue.Id}\" needs 1 to {StatementGateTask.MaximumQuoteLength} characters.");
            return null;
        }
        var found = UniqueQuote.Find(extraction.Text, issue.Quote, out var start);
        var problem = found switch
        {
            UniqueQuoteResult.Missing => "does not occur in the article text.",
            UniqueQuoteResult.Ambiguous => "occurs more than once in the article text. Quote a longer, unique passage.",
            UniqueQuoteResult.SplitsCharacter => "splits a character. Quote whole characters.",
            _ => null
        };
        if (problem is not null)
        {
            errors.Add($"The quote \"{Shorten(issue.Quote)}\" for \"{issue.Id}\" {problem}");
            return null;
        }
        var span = new DocumentTextSpan(extraction, start, issue.Quote.Length);
        return new(issue.Id, span.ExtractionId, span.Start, span.Length);
    }

    private static void CheckConfidence(double value, string name, List<string> errors)
    {
        if (!double.IsFinite(value) || value < 0 || value > 1) errors.Add($"{name} must be a number from 0 to 1.");
    }

    private static string Shorten(string? value) => Truncate(value ?? "", 80);

    private static string Truncate(string value, int maximumLength)
    {
        if (value.Length <= maximumLength) return value;
        var length = maximumLength - 3;
        if (char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length] + "...";
    }

    private static StatementGateResolution Rejected(IEnumerable<string> errors, bool citationErrors) =>
        new(null, [], [.. errors.Take(AnalysisRun.MaximumValidationErrors)
            .Select(error => Truncate(error, AnalysisRun.MaximumValidationErrorLength))], citationErrors);
}
