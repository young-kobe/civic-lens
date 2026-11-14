using CivicLens.Application.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.ExecutiveActions;

namespace CivicLens.Application.ExecutiveActions;

/// <summary>The approval checks of an executive action revision that holds a summary. Core owns the checks that need no reading grade: each quote
/// is a span of the body text with enough words. This class adds the grade check, which lives in Application. The sentences read at
/// <see cref="AiTextRules.MaximumGrade"/> or below as one passage, the rule that the step applied when it wrote them. It follows <see cref="Actions.ActionTextApproval"/>.</summary>
public static class ExecutiveSummaryApproval
{
    public static void Validate(ExecutiveActionRevision revision, IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        ArgumentNullException.ThrowIfNull(revision);
        revision.ValidateForApproval(extractions);
        if (revision.Summary is { } summary) AiTextRules.RequireReadablePassage(summary.Sentences.Select(sentence => sentence.Sentence));
    }
}
