using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using CivicLens.Core.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Analysis;

public static class DocumentChangeDraftResolver
{
    public static DocumentChangeDraftResolution Resolve(string outputJson, DocumentChangeDraftEvidence evidence, DocumentChangeDraftCatalog catalog,
        string draftId, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(catalog);
        var output = Parse(outputJson);
        if (output is null) return Rejected(["The output is not valid JSON for the draft schema."], citationErrors: false);
        var errors = new List<string>();
        var citationErrors = new List<string>();
        CheckText(output.Headline, DocumentChangeDraftRevision.MaximumHeadlineLength, "headline", required: true, errors);
        CheckText(output.Summary, DocumentChangeDraftRevision.MaximumSummaryLength, "summary", required: true, errors);
        CheckText(output.Significance, DocumentChangeDraftRevision.MaximumSignificanceLength, "significance", required: false, errors);
        CheckText(output.Limits, DocumentChangeDraftRevision.MaximumLimitsLength, "limits", required: true, errors);
        CheckText(output.Institution, DocumentChangeDraftRevision.MaximumInstitutionLength, "institution", required: true, errors);
        CheckSelections(output.OfficialIds, catalog.Officials.Select(official => official.Id), "officialIds", errors);
        CheckSelections(output.IssueIds, catalog.IssueIds, "issueIds", errors);
        CheckConfidence(output.Confidence, "confidence", errors);
        var citations = ResolveCitations(output.Citations, evidence, errors, citationErrors);
        var changeDate = ResolveChangeDate(output.ChangeDate, output.Citations, citations, errors, citationErrors);
        if (errors.Count > 0 || citations.IsDefault) return Rejected(errors, citationErrors.Count > 0);
        var revision = new DocumentChangeDraftRevision(draftId, 1, evidence.Comparison.Comparison.ComparisonId,
            ReviewAuthor.AnalysisSubject, createdAtUtc, output.Headline, output.Summary, Normalize(output.Significance),
            output.Limits, output.Institution, changeDate?.Date, changeDate?.Evidence, Ids(output.OfficialIds), Ids(output.IssueIds), citations);
        return new(revision, [], false);
    }

    private static DocumentChangeDraftOutput? Parse(string outputJson)
    {
        try
        {
            var output = JsonSerializer.Deserialize<DocumentChangeDraftOutput>(outputJson, OutputJson);
            if (output is null || output.OfficialIds.IsDefault || output.IssueIds.IsDefault || output.Citations.IsDefault) return null;
            var hasNull = output.Citations.Any(citation => citation is null) || output.OfficialIds.Any(item => item is null) ||
                output.IssueIds.Any(item => item is null);
            return hasNull ? null : output;
        }
        catch (JsonException) { return null; }
    }

    private static ImmutableArray<SourceSpan> ResolveCitations(ImmutableArray<DocumentChangeDraftOutputCitation> citations,
        DocumentChangeDraftEvidence evidence, List<string> errors, List<string> citationErrors)
    {
        if (citations.IsEmpty || citations.Length > DocumentChangeDraftRevision.MaximumCitations)
        {
            errors.Add($"Give 1 to {DocumentChangeDraftRevision.MaximumCitations} citations.");
            return default;
        }
        var resolved = ImmutableArray.CreateBuilder<SourceSpan>(citations.Length);
        for (var index = 0; index < citations.Length; index++)
        {
            var citation = ResolveCitation(citations[index], index, evidence, errors, citationErrors);
            if (citation is null) continue;
            if (resolved.Contains(citation)) errors.Add($"Citation {index} repeats an earlier citation.");
            else resolved.Add(citation);
        }
        return resolved.Count == citations.Length ? resolved.MoveToImmutable() : default;
    }

    private static SourceSpan? ResolveCitation(DocumentChangeDraftOutputCitation citation, int index, DocumentChangeDraftEvidence evidence,
        List<string> errors, List<string> citationErrors)
    {
        var hunks = evidence.Comparison.Comparison.Hunks;
        var hunkIndex = ParseHunkIndex(citation.HunkId, hunks.Length);
        if (hunkIndex is null)
        {
            AddCitationError($"Citation {index} names an unknown hunk \"{Shorten(citation.HunkId)}\".", errors, citationErrors);
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
            AddCitationError($"Citation {index} has side \"{Shorten(citation.Side)}\". Use \"before\" or \"after\".", errors, citationErrors);
            return null;
        }
        if (string.IsNullOrEmpty(citation.Quote) || citation.Quote.Length > DocumentChangeDraftRevision.MaximumCitationLength)
        {
            AddCitationError($"Citation {index} needs a quote of 1 to {DocumentChangeDraftRevision.MaximumCitationLength} characters.", errors, citationErrors);
            return null;
        }
        return Locate(citation, index, window, windowStart, extraction, errors, citationErrors);
    }

    private static SourceSpan? Locate(DocumentChangeDraftOutputCitation citation, int index, string window, int windowStart,
        DocumentExtraction extraction, List<string> errors, List<string> citationErrors)
    {
        var found = UniqueQuote.Find(window, citation.Quote, out var first);
        if (found == UniqueQuoteResult.Missing)
        {
            AddCitationError($"Citation {index}: the quote \"{Shorten(citation.Quote)}\" does not occur in the {citation.Side} window of {citation.HunkId}.", errors, citationErrors);
            return null;
        }
        if (found == UniqueQuoteResult.Ambiguous)
        {
            AddCitationError($"Citation {index}: the quote \"{Shorten(citation.Quote)}\" occurs more than once in the {citation.Side} window of {citation.HunkId}. Quote a longer, unique passage.", errors, citationErrors);
            return null;
        }
        if (found == UniqueQuoteResult.SplitsCharacter)
        {
            AddCitationError($"Citation {index}: the quote boundaries split a character. Quote whole characters.", errors, citationErrors);
            return null;
        }
        var span = new DocumentTextSpan(extraction, windowStart + first, citation.Quote.Length);
        if (span.Quote != citation.Quote) throw new InvalidOperationException("Comparison window does not match its extraction.");
        return new(span.ExtractionId, span.Start, span.Length);
    }

    private static (DateOnly Date, SourceSpan Evidence)? ResolveChangeDate(DocumentChangeDraftOutputChangeDate? changeDate,
        ImmutableArray<DocumentChangeDraftOutputCitation> quotes, ImmutableArray<SourceSpan> citations, List<string> errors,
        List<string> citationErrors)
    {
        if (changeDate is null) return null;
        CheckConfidence(changeDate.Confidence, "changeDate.confidence", errors);
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
            AddCitationError($"changeDate: citation {changeDate.CitationIndex} does not state {changeDate.Date}. Cite the passage that states the date.", errors, citationErrors);
            return null;
        }
        return (date, citations[changeDate.CitationIndex]);
    }

    private static int? ParseHunkIndex(string? hunkId, int count)
    {
        if (hunkId is not { Length: > 1 } || hunkId[0] != 'h' || hunkId[1] == '0') return null;
        return int.TryParse(hunkId.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            number >= 1 && number <= count ? number - 1 : null;
    }

    private static void CheckSelections(ImmutableArray<DocumentChangeDraftOutputSelection> values, IEnumerable<string> allowed, string name,
        List<string> errors)
    {
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (values.Length > DocumentChangeDraftRevision.MaximumSelections)
            errors.Add($"{name} can hold at most {DocumentChangeDraftRevision.MaximumSelections} values.");
        foreach (var value in values)
        {
            CheckConfidence(value.Confidence, $"{name} confidence for \"{Shorten(value.Id)}\"", errors);
            if (value.Id is null || !allowedSet.Contains(value.Id)) errors.Add($"{name} holds \"{Shorten(value.Id)}\", which is not a listed ID.");
            else if (!seen.Add(value.Id)) errors.Add($"{name} repeats \"{value.Id}\".");
        }
    }

    private static ImmutableArray<string> Ids(ImmutableArray<DocumentChangeDraftOutputSelection> selections) =>
        [.. selections.Select(selection => selection.Id)];

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DocumentChangeDraftResolution Rejected(IEnumerable<string> errors, bool citationErrors) =>
        new(null, Bound(errors), citationErrors);
}
