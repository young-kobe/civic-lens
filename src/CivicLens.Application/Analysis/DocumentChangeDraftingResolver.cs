using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Application.Analysis;

public static class DocumentChangeDraftingResolver
{
    private static readonly JsonSerializerOptions OutputJson = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public static DocumentChangeDraftingResolution Resolve(string outputJson, DocumentChangeAnalysisEvidence evidence, DocumentChangeAnalysisCatalog catalog,
        string draftId, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(catalog);
        var output = Parse(outputJson);
        if (output is null) return Rejected(["The output is not valid JSON for the draft schema."], citationErrors: false);
        var errors = new List<string>();
        CheckText(output.Headline, DocumentChangeDraftRevision.MaximumHeadlineLength, "headline", required: true, errors);
        CheckText(output.Summary, DocumentChangeDraftRevision.MaximumSummaryLength, "summary", required: true, errors);
        CheckText(output.Significance, DocumentChangeDraftRevision.MaximumSignificanceLength, "significance", required: false, errors);
        CheckText(output.Limits, DocumentChangeDraftRevision.MaximumLimitsLength, "limits", required: true, errors);
        CheckText(output.Institution, DocumentChangeDraftRevision.MaximumInstitutionLength, "institution", required: true, errors);
        CheckSelections(output.OfficialIds, catalog.Officials.Select(official => official.Id), "officialIds", errors);
        CheckSelections(output.IssueIds, catalog.IssueIds, "issueIds", errors);
        var textErrors = errors.Count;
        var citations = ResolveCitations(output.Citations, evidence, errors);
        var changeDate = ResolveChangeDate(output.ChangeDate, output.Citations, citations, errors);
        var citationErrors = errors.Count > textErrors;
        if (errors.Count > 0 || citations.IsDefault) return Rejected(errors, citationErrors);
        var revision = new DocumentChangeDraftRevision(draftId, 1, evidence.Comparison.Comparison.ComparisonId,
            ReviewAuthor.AnalysisSubject, createdAtUtc, output.Headline, output.Summary, Normalize(output.Significance),
            output.Limits, output.Institution, changeDate?.Date, changeDate?.Evidence, output.OfficialIds, output.IssueIds, citations);
        return new(revision, [], false);
    }

    private static DocumentChangeDraftingOutput? Parse(string outputJson)
    {
        try
        {
            var output = JsonSerializer.Deserialize<DocumentChangeDraftingOutput>(outputJson, OutputJson);
            if (output is null || output.OfficialIds.IsDefault || output.IssueIds.IsDefault || output.Citations.IsDefault) return null;
            return output.Citations.Any(citation => citation is null) ? null : output;
        }
        catch (JsonException) { return null; }
    }

    private static ImmutableArray<DocumentChangeCitation> ResolveCitations(ImmutableArray<DocumentChangeDraftingOutputCitation> citations,
        DocumentChangeAnalysisEvidence evidence, List<string> errors)
    {
        if (citations.IsEmpty || citations.Length > DocumentChangeDraftRevision.MaximumCitations)
        {
            errors.Add($"Give 1 to {DocumentChangeDraftRevision.MaximumCitations} citations.");
            return default;
        }
        var resolved = ImmutableArray.CreateBuilder<DocumentChangeCitation>(citations.Length);
        for (var index = 0; index < citations.Length; index++)
        {
            var citation = ResolveCitation(citations[index], index, evidence, errors);
            if (citation is null) continue;
            if (resolved.Contains(citation)) errors.Add($"Citation {index} repeats an earlier citation.");
            else resolved.Add(citation);
        }
        return resolved.Count == citations.Length ? resolved.MoveToImmutable() : default;
    }

    private static DocumentChangeCitation? ResolveCitation(DocumentChangeDraftingOutputCitation citation, int index, DocumentChangeAnalysisEvidence evidence,
        List<string> errors)
    {
        var hunks = evidence.Comparison.Comparison.Hunks;
        var hunkIndex = ParseHunkIndex(citation.HunkId, hunks.Length);
        if (hunkIndex is null)
        {
            errors.Add($"Citation {index} names an unknown hunk \"{Shorten(citation.HunkId)}\".");
            return null;
        }
        var hunk = hunks[hunkIndex.Value];
        var (window, windowStart, extraction) = citation.Side switch
        {
            "before" => (hunk.BeforeContext, hunk.BeforeContextStart, evidence.Before),
            "after" => (hunk.AfterContext, hunk.AfterContextStart, evidence.After),
            _ => (null, 0, null)
        };
        if (window is null || extraction is null)
        {
            errors.Add($"Citation {index} has side \"{Shorten(citation.Side)}\". Use \"before\" or \"after\".");
            return null;
        }
        if (string.IsNullOrEmpty(citation.Quote) || citation.Quote.Length > DocumentChangeDraftRevision.MaximumCitationLength)
        {
            errors.Add($"Citation {index} needs a quote of 1 to {DocumentChangeDraftRevision.MaximumCitationLength} characters.");
            return null;
        }
        return Locate(citation, index, window, windowStart, extraction, errors);
    }

    private static DocumentChangeCitation? Locate(DocumentChangeDraftingOutputCitation citation, int index, string window, int windowStart,
        DocumentExtraction extraction, List<string> errors)
    {
        var first = window.IndexOf(citation.Quote, StringComparison.Ordinal);
        if (first < 0)
        {
            errors.Add($"Citation {index}: the quote \"{Shorten(citation.Quote)}\" does not occur in the {citation.Side} window of {citation.HunkId}.");
            return null;
        }
        if (window.IndexOf(citation.Quote, first + 1, StringComparison.Ordinal) >= 0)
        {
            errors.Add($"Citation {index}: the quote \"{Shorten(citation.Quote)}\" occurs more than once in the {citation.Side} window of {citation.HunkId}. Quote a longer, unique passage.");
            return null;
        }
        if (char.IsLowSurrogate(citation.Quote[0]) || char.IsHighSurrogate(citation.Quote[^1]))
        {
            errors.Add($"Citation {index}: the quote boundaries split a character. Quote whole characters.");
            return null;
        }
        var span = new DocumentTextSpan(extraction, windowStart + first, citation.Quote.Length);
        if (span.Quote != citation.Quote) throw new InvalidOperationException("Comparison window does not match its extraction.");
        return new(span.ExtractionId, span.Start, span.Length);
    }

    private static (DateOnly Date, DocumentChangeCitation Evidence)? ResolveChangeDate(DocumentChangeDraftingOutputChangeDate? changeDate,
        ImmutableArray<DocumentChangeDraftingOutputCitation> quotes, ImmutableArray<DocumentChangeCitation> citations, List<string> errors)
    {
        if (changeDate is null) return null;
        if (!DateOnly.TryParseExact(changeDate.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add("changeDate.date must be a calendar date in yyyy-MM-dd form.");
            return null;
        }
        if (citations.IsDefault) return null;
        if (changeDate.CitationIndex < 0 || changeDate.CitationIndex >= citations.Length)
        {
            errors.Add("changeDate.citationIndex must name one of the citations.");
            return null;
        }
        if (!StatesDate(quotes[changeDate.CitationIndex].Quote, date))
        {
            errors.Add($"changeDate: citation {changeDate.CitationIndex} does not state {changeDate.Date}. Cite the passage that states the date.");
            return null;
        }
        return (date, citations[changeDate.CitationIndex]);
    }

    private static bool StatesDate(string quote, DateOnly date) =>
        new[] { "yyyy-MM-dd", "MMMM d, yyyy", "MMM d, yyyy", "MMM. d, yyyy", "d MMMM yyyy", "M/d/yyyy", "MM/dd/yyyy" }
            .Any(format => quote.Contains(date.ToString(format, CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase));

    private static int? ParseHunkIndex(string? hunkId, int count)
    {
        if (hunkId is not { Length: > 1 } || hunkId[0] != 'h' || hunkId[1] == '0') return null;
        return int.TryParse(hunkId.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            number >= 1 && number <= count ? number - 1 : null;
    }

    private static void CheckText(string? value, int maximumLength, string name, bool required, List<string> errors)
    {
        if (value is null && !required) return;
        if (string.IsNullOrWhiteSpace(value) && required) errors.Add($"{name} is required.");
        else if (value is not null && (value.Length > maximumLength || value.Contains('\0')))
            errors.Add($"{name} must be at most {maximumLength} characters with no NUL character.");
    }

    private static void CheckSelections(ImmutableArray<string> values, IEnumerable<string> allowed, string name,
        List<string> errors)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (values.Length > DocumentChangeDraftRevision.MaximumSelections)
            errors.Add($"{name} can hold at most {DocumentChangeDraftRevision.MaximumSelections} values.");
        foreach (var value in values)
        {
            if (value is null || !allowedSet.Contains(value)) errors.Add($"{name} holds \"{Shorten(value)}\", which is not a listed ID.");
            else if (!seen.Add(value)) errors.Add($"{name} repeats \"{value}\".");
        }
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static string Shorten(string? value) => Truncate(value ?? "", 80);

    private static string Truncate(string value, int maximumLength)
    {
        if (value.Length <= maximumLength) return value;
        var length = maximumLength - 3;
        if (char.IsHighSurrogate(value[length - 1])) length--;
        return value[..length] + "...";
    }

    private static DocumentChangeDraftingResolution Rejected(IEnumerable<string> errors, bool citationErrors) =>
        new(null, [.. errors.Take(AnalysisRun.MaximumValidationErrors)
            .Select(error => Truncate(error, AnalysisRun.MaximumValidationErrorLength))], citationErrors);
}
