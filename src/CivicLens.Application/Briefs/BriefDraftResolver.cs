using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Core.Briefs;
using CivicLens.Core.Review;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Briefs;

public static class BriefDraftResolver
{
    public static AnalysisResolution<BriefRevision> Resolve(string outputJson, BriefDraftContext context, string runId,
        DateTimeOffset createdAtUtc)
    {
        var output = Parse(outputJson);
        if (output is null)
            return AnalysisResolution<BriefRevision>.Reject(["The output is not valid JSON for the brief schema."], false);
        var errors = new List<string>();
        var citationErrors = new List<string>();
        CheckText(output.Headline, BriefRevision.MaximumHeadlineLength, "headline", required: true, errors);
        CheckConfidence(output.Confidence, "confidence", errors);
        CheckNamesOutsideFacts(output.Headline, "headline", context.ExcludedNames, context.ListedOfficials.Values, errors);
        CheckParagraphs(output.Paragraphs, context, errors, citationErrors);
        if (errors.Count > 0) return AnalysisResolution<BriefRevision>.Reject(Bound(errors), citationErrors.Count > 0);
        return AnalysisResolution<BriefRevision>.Accept(Assemble(output, context, runId, createdAtUtc));
    }

    private static void CheckParagraphs(ImmutableArray<BriefDraftOutputParagraph> paragraphs, BriefDraftContext context, List<string> errors,
        List<string> citationErrors)
    {
        if (paragraphs.IsDefault || paragraphs.Length is < 1 or > BriefRevision.MaximumParagraphs)
        {
            errors.Add($"Write 1 to {BriefRevision.MaximumParagraphs} paragraphs.");
            return;
        }
        var known = context.Selection.Sent.Select(item => item.CitationId).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < paragraphs.Length; index++)
        {
            var name = $"paragraph {index + 1}";
            CheckText(paragraphs[index].Text, BriefRevision.MaximumParagraphLength, name, required: true, errors);
            CheckNamesOutsideFacts(paragraphs[index].Text ?? "", name, context.ExcludedNames, context.ListedOfficials.Values, errors);
            CheckCitationIds(paragraphs[index].CitationIds, known, name, errors, citationErrors);
        }
        CheckCitationCount(paragraphs, errors, citationErrors);
    }

    private static void CheckCitationCount(ImmutableArray<BriefDraftOutputParagraph> paragraphs, List<string> errors, List<string> citationErrors)
    {
        var distinct = paragraphs.Where(paragraph => !paragraph.CitationIds.IsDefaultOrEmpty)
            .SelectMany(paragraph => paragraph.CitationIds).Distinct(StringComparer.Ordinal).Count();
        if (distinct > BriefRevision.MaximumCitations)
            AddCitationError($"The brief cites {distinct} IDs. Cite at most {BriefRevision.MaximumCitations} IDs.", errors, citationErrors);
    }

    private static void CheckCitationIds(ImmutableArray<string> ids, HashSet<string> known, string name, List<string> errors,
        List<string> citationErrors)
    {
        if (ids.IsDefaultOrEmpty)
        {
            AddCitationError($"The {name} cites no ID. Cite at least one listed ID.", errors, citationErrors);
            return;
        }
        foreach (var id in ids.Where(id => !known.Contains(id)).Distinct(StringComparer.Ordinal))
            AddCitationError($"The {name} cites \"{Shorten(id)}\", which is not a listed ID. Cite only the listed IDs.", errors, citationErrors);
        foreach (var id in ids.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key))
            AddCitationError($"The {name} cites \"{Shorten(id)}\" more than once. List each ID once in a paragraph.", errors, citationErrors);
    }

    private static BriefRevision Assemble(BriefDraftOutput output, BriefDraftContext context, string runId, DateTimeOffset createdAtUtc)
    {
        var current = context.Evidence.Current;
        var paragraphs = output.Paragraphs.Select(paragraph => new BriefParagraph(paragraph.Text, paragraph.CitationIds)).ToImmutableArray();
        var modelCitations = CitationsOf(paragraphs, context);
        var (headline, text, citations, origin, suggestion) = current?.TextOrigin == ReviewTextOrigin.Person
            ? (current.Headline, current.Paragraphs, current.Citations, ReviewTextOrigin.Person,
                new BriefTextSuggestion(output.Headline, paragraphs, modelCitations, runId, context.Fingerprint))
            : (output.Headline, paragraphs, modelCitations, ReviewTextOrigin.Model, (BriefTextSuggestion?)null);
        var revision = new BriefRevision(context.Evidence.Window.RecordId, (current?.RevisionNumber ?? 0) + 1, ReviewAuthor.AnalysisSubject,
            createdAtUtc, context.Evidence.Window, context.Fingerprint, headline, origin, suggestion, text, citations,
            new(context.Selection.Considered, context.Selection.Sent.Length, citations.Length));
        revision.ValidateAsAnalysisRevision(current);
        return revision;
    }

    private static ImmutableArray<BriefCitation> CitationsOf(ImmutableArray<BriefParagraph> paragraphs, BriefDraftContext context)
    {
        var cited = paragraphs.SelectMany(paragraph => paragraph.CitationIds).ToHashSet(StringComparer.Ordinal);
        return [.. context.Selection.Sent.Where(item => cited.Contains(item.CitationId))
            .Select(item => new BriefCitation(item.CitationId, item.Record.Kind, item.Record.RecordId, item.Record.RevisionNumber))];
    }

    private static BriefDraftOutput? Parse(string outputJson)
    {
        try
        {
            return JsonSerializer.Deserialize<BriefDraftOutput>(outputJson, OutputJson);
        }
        catch (JsonException) { return null; }
    }
}
