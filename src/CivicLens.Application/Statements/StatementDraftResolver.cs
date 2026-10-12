using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Statements;

public static class StatementDraftResolver
{
    public static StatementDraftResolution Resolve(string outputJson, StatementDraftEvidence evidence, ImmutableArray<AnalysisOfficial> officials,
        DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (officials.IsDefaultOrEmpty) throw new ArgumentException("A statement draft needs at least one covered official.", nameof(officials));
        var output = Parse(outputJson);
        if (output is null) return Rejected(["The output is not valid JSON for the statement schema."], citationErrors: false);
        var extraction = evidence.Article.Extraction;
        var errors = new List<string>();
        CheckText(output.Headline, StatementDraftRevision.MaximumHeadlineLength, "headline", required: true, errors);
        CheckText(output.Summary, StatementDraftRevision.MaximumSummaryLength, "summary", required: true, errors);
        CheckConfidence(output.Confidence, "confidence", errors);
        var speakers = ResolveSpeakers(output.SpeakerOfficialIds, officials, errors);
        var others = ResolveOtherSpeakers(output.OtherSpeakers, extraction.Text, errors);
        var fieldErrors = errors.Count;
        var date = ResolveDate(output.StatementDate, extraction, errors);
        var quotations = ResolveQuotations(output.Quotations, extraction, errors);
        if (errors.Count > 0) return Rejected(errors, citationErrors: errors.Count > fieldErrors);
        var revision = new StatementDraftRevision(evidence.Article.CandidateId, 1, ReviewAuthor.AnalysisSubject, createdAtUtc,
            output.Headline, output.Summary, date?.Date, date?.Evidence, speakers, others, evidence.CarryOver.IssueIds,
            evidence.CarryOver.PolicyAreaIds, quotations, BillNumberFinder.Find(extraction.Text));
        return new(revision, [], false);
    }

    private static StatementDraftOutput? Parse(string outputJson)
    {
        try
        {
            var output = JsonSerializer.Deserialize<StatementDraftOutput>(outputJson, OutputJson);
            if (output is null || output.OtherSpeakers.IsDefault || output.Quotations.IsDefault) return null;
            var hasNull = output.OtherSpeakers.Any(item => item is null) || output.Quotations.Any(item => item is null) ||
                !output.SpeakerOfficialIds.IsDefault && output.SpeakerOfficialIds.Any(item => item is null);
            return hasNull ? null : output;
        }
        catch (JsonException) { return null; }
    }

    private static ImmutableArray<string> ResolveSpeakers(ImmutableArray<StatementDraftOutputSelection> chosen,
        ImmutableArray<AnalysisOfficial> officials, List<string> errors)
    {
        if (officials.Length == 1)
        {
            if (!chosen.IsDefault) errors.Add("speakerOfficialIds is not a field of this answer. Civic Lens sets the speaker.");
            return [officials[0].Id];
        }
        if (chosen.IsDefault)
        {
            errors.Add("speakerOfficialIds is required.");
            return [];
        }
        if (chosen.IsEmpty) errors.Add("speakerOfficialIds needs at least one listed official.");
        var listed = officials.Select(official => official.Id).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var speaker in chosen)
        {
            CheckConfidence(speaker.Confidence, $"speakerOfficialIds confidence for \"{Shorten(speaker.Id)}\"", errors);
            if (speaker.Id is null || !listed.Contains(speaker.Id))
                errors.Add($"speakerOfficialIds holds \"{Shorten(speaker.Id)}\", which is not a listed ID.");
            else if (!seen.Add(speaker.Id)) errors.Add($"speakerOfficialIds repeats \"{speaker.Id}\".");
        }
        return [.. chosen.Select(speaker => speaker.Id)];
    }

    private static ImmutableArray<string> ResolveOtherSpeakers(ImmutableArray<StatementDraftOutputSpeaker> speakers, string articleText,
        List<string> errors)
    {
        if (speakers.Length > StatementDraftRevision.MaximumOtherSpeakers)
            errors.Add($"otherSpeakers can hold at most {StatementDraftRevision.MaximumOtherSpeakers} names.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var speaker in speakers)
        {
            CheckConfidence(speaker.Confidence, $"otherSpeakers confidence for \"{Shorten(speaker.Name)}\"", errors);
            if (!IsPlainName(speaker.Name))
                errors.Add($"otherSpeakers holds \"{Shorten(speaker.Name)}\". A name has 1 to {StatementDraftRevision.MaximumOtherSpeakerLength} characters, no NUL character, and no space at either end.");
            else if (!articleText.Contains(speaker.Name, StringComparison.Ordinal))
                errors.Add($"The name \"{Shorten(speaker.Name)}\" in otherSpeakers does not occur in the article text.");
            else if (!seen.Add(speaker.Name)) errors.Add($"otherSpeakers repeats \"{Shorten(speaker.Name)}\".");
        }
        return [.. speakers.Select(speaker => speaker.Name)];
    }

    private static bool IsPlainName(string? name) =>
        !string.IsNullOrEmpty(name) && name.Length <= StatementDraftRevision.MaximumOtherSpeakerLength && !name.Contains('\0') &&
        name == name.Trim();

    private static (DateOnly Date, StatementSpan Evidence)? ResolveDate(StatementDraftOutputDate? statementDate, DocumentExtraction extraction,
        List<string> errors)
    {
        if (statementDate is null) return null;
        CheckConfidence(statementDate.Confidence, "statementDate confidence", errors);
        if (!DateOnly.TryParseExact(statementDate.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            errors.Add("statementDate.date must be a calendar date in yyyy-MM-dd form.");
            return null;
        }
        var span = ArticleQuote.Locate(extraction, statementDate.Quote, "statementDate", StatementDraftRevision.MaximumQuotationLength, errors);
        if (span is null) return null;
        if (!StatesDate(span.Quote, date))
        {
            errors.Add($"statementDate: the quote does not state {statementDate.Date}. Quote the passage that states the date.");
            return null;
        }
        return (date, new(span.ExtractionId, span.Start, span.Length));
    }

    private static ImmutableArray<StatementQuotation> ResolveQuotations(ImmutableArray<StatementDraftOutputQuotation> quotations,
        DocumentExtraction extraction, List<string> errors)
    {
        if (quotations.IsEmpty || quotations.Length > StatementDraftRevision.MaximumQuotations)
        {
            errors.Add($"Give 1 to {StatementDraftRevision.MaximumQuotations} quotations.");
            return [];
        }
        var quoted = new QuotedText(extraction.Text);
        var resolved = new List<StatementQuotation>(quotations.Length);
        for (var index = 0; index < quotations.Length; index++)
        {
            var label = $"quotations[{index}]";
            CheckConfidence(quotations[index].Confidence, $"{label} confidence", errors);
            var span = ArticleQuote.Locate(extraction, quotations[index].Quote, label, StatementDraftRevision.MaximumQuotationLength, errors);
            if (span is null) continue;
            if (resolved.Any(item => item.Span.Start == span.Start && item.Span.Length == span.Length)) errors.Add($"{label} repeats an earlier quotation.");
            else resolved.Add(new(new(span.ExtractionId, span.Start, span.Length), quoted.KindOf(span.Start, span.Length)));
        }
        return [.. resolved];
    }

    private static StatementDraftResolution Rejected(IEnumerable<string> errors, bool citationErrors) =>
        new(null, Bound(errors), citationErrors);
}
