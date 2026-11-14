using CivicLens.Application.Analysis;
using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

/// <summary>
/// The approval checks of an action revision, for code and for a person. Core owns the checks that need no reading grade: each citation names a
/// real span, a text quote lies in the body of the bill text and has enough words, and the model text is the join of its sentences.
/// This class adds the check that Core cannot make, because the reading grade lives in Application. Model text reads at
/// <see cref="AiTextRules.MaximumGrade"/> or below as one passage, the rule that the draft step applied when it wrote it. For text drawn from the
/// bill text, the passage is the headline and all quoted sentences. For text drawn from the CRS summary, it is the headline and the summary.
/// A person's own wording has no grade check, because a person decides.
/// </summary>
public static class ActionTextApproval
{
    public static void Validate(ActionDraftRevision revision, IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        ArgumentNullException.ThrowIfNull(revision);
        revision.ValidateForApproval(extractions);
        if (revision.TextOrigin == ReviewTextOrigin.Model) AiTextRules.RequireReadablePassage(PassageOf(revision));
    }

    private static IEnumerable<string> PassageOf(ActionDraftRevision revision) => revision.TextPackageId is null
        ? [revision.Headline, revision.Summary]
        : revision.TextCitations.Select(citation => citation.Sentence).Prepend(revision.Headline);
}
