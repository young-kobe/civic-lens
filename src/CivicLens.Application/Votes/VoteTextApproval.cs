using CivicLens.Application.Analysis;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

/// <summary>The approval checks of a vote revision, for code and for a person. Core owns the checks that need no reading grade. This class adds
/// the grade check, which lives in Application: model text, the headline and the summary, reads at <see cref="AiTextRules.MaximumGrade"/> or below
/// as one passage. A person's own wording has no grade check, because a person decides. It follows <see cref="Actions.ActionTextApproval"/>.</summary>
public static class VoteTextApproval
{
    public static void Validate(VoteDraftRevision revision, IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        ArgumentNullException.ThrowIfNull(revision);
        revision.ValidateForApproval(extractions);
        if (revision.TextOrigin == ReviewTextOrigin.Model) AiTextRules.RequireReadablePassage([revision.Headline, revision.Summary]);
    }
}
