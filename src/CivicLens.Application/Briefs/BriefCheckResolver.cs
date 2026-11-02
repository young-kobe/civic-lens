using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Analysis;
using CivicLens.Core.Briefs;
using static CivicLens.Application.Analysis.AnalysisAnswerRules;

namespace CivicLens.Application.Briefs;

public static class BriefCheckResolver
{
    public static AnalysisResolution<BriefCheckResult> Resolve(string outputJson, BriefRevision revision)
    {
        var output = Parse(outputJson);
        if (output is null)
            return AnalysisResolution<BriefCheckResult>.Reject(["The output is not valid JSON for the brief check schema."], false);
        var errors = new List<string>();
        var citationErrors = new List<string>();
        var recommendation = CheckRecommendation(output, errors);
        var flags = CheckFlags(output.Flags, revision, errors, citationErrors);
        if (errors.Count > 0) return AnalysisResolution<BriefCheckResult>.Reject(Bound(errors), citationErrors.Count > 0);
        return AnalysisResolution<BriefCheckResult>.Accept(new(revision.RevisionNumber, recommendation, flags));
    }

    private static BriefCheckRecommendation CheckRecommendation(BriefCheckOutput output, List<string> errors)
    {
        if (!TryParse(output.Recommendation, out BriefCheckRecommendation recommendation))
        {
            errors.Add($"recommendation must be one of: {string.Join(", ", Enum.GetNames<BriefCheckRecommendation>())}.");
            return default;
        }
        var hasFlags = !output.Flags.IsDefaultOrEmpty;
        if (recommendation == BriefCheckRecommendation.ApproveRecommended && hasFlags)
            errors.Add("A recommendation of ApproveRecommended needs an empty flags list.");
        if (recommendation == BriefCheckRecommendation.NeedsAttention && !hasFlags)
            errors.Add("A recommendation of NeedsAttention needs at least one flag.");
        return recommendation;
    }

    private static ImmutableArray<BriefCheckFlag> CheckFlags(ImmutableArray<BriefCheckOutputFlag> flags, BriefRevision revision,
        List<string> errors, List<string> citationErrors)
    {
        if (flags.IsDefault) return [];
        if (flags.Length > BriefCheckResult.MaximumFlags)
            errors.Add($"The answer has {flags.Length} flags. Write at most {BriefCheckResult.MaximumFlags}.");
        var known = revision.Citations.Select(citation => citation.Id).ToHashSet(StringComparer.Ordinal);
        var checkedFlags = ImmutableArray.CreateBuilder<BriefCheckFlag>();
        for (var index = 0; index < flags.Length; index++)
            checkedFlags.Add(CheckFlag(flags[index], $"flag {index + 1}", revision.Paragraphs.Length, known, errors, citationErrors));
        return checkedFlags.ToImmutable();
    }

    private static BriefCheckFlag CheckFlag(BriefCheckOutputFlag flag, string name, int paragraphCount, HashSet<string> known,
        List<string> errors, List<string> citationErrors)
    {
        if (flag.ParagraphIndex < 0 || flag.ParagraphIndex >= paragraphCount)
            errors.Add($"The {name} names paragraph {flag.ParagraphIndex}. Use a paragraph number from 0 to {paragraphCount - 1}.");
        if (!TryParse(flag.Code, out BriefCheckFlagCode code))
            errors.Add($"The {name} has the code \"{Shorten(flag.Code)}\". Use one of: {string.Join(", ", Enum.GetNames<BriefCheckFlagCode>())}.");
        if (flag.CitationId is not null && !known.Contains(flag.CitationId))
            AddCitationError($"The {name} cites \"{Shorten(flag.CitationId)}\", which is not a listed ID. Use a listed ID or null.", errors, citationErrors);
        CheckText(flag.Note, BriefCheckFlag.MaximumNoteLength, $"The note of {name}", required: true, errors);
        return new(flag.ParagraphIndex, code, flag.CitationId, flag.Note);
    }

    private static bool TryParse<TEnum>(string? value, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        return value is not null && Enum.TryParse(value, ignoreCase: true, out result) && Enum.IsDefined(result) &&
            !int.TryParse(value, out _);
    }

    private static BriefCheckOutput? Parse(string outputJson)
    {
        try
        {
            return JsonSerializer.Deserialize<BriefCheckOutput>(outputJson, OutputJson);
        }
        catch (JsonException) { return null; }
    }
}
