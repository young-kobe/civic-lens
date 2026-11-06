using CivicLens.Core.Actions;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

/// <summary>
/// The approval checks of an action revision, for code and for a person. Core owns the checks that need no reading grade: each citation names a
/// real span, a text quote lies in the body of the bill text and has enough words, and the model text is the join of its sentences.
/// This class adds the check that Core cannot make, because the reading grade lives in Application. The headline and the sentences of model text
/// drawn from the bill text read at <see cref="ReadingGrade.MaximumGrade"/> or below as one passage, the rule that the draft step applied when it wrote them.
/// A person's own wording has no grade check, because a person decides.
/// </summary>
public static class ActionTextApproval
{
    public static void Validate(ActionDraftRevision revision, IReadOnlyDictionary<string, DocumentExtraction> extractions)
    {
        ArgumentNullException.ThrowIfNull(revision);
        revision.ValidateForApproval(extractions);
        if (revision is { TextPackageId: not null, TextOrigin: ReviewTextOrigin.Model }) RequireReadablePassage(revision);
    }

    private static void RequireReadablePassage(ActionDraftRevision revision)
    {
        var pieces = revision.TextCitations.Select(citation => citation.Sentence).Prepend(revision.Headline).ToArray();
        var grade = ReadingGrade.RoundedPassage(pieces);
        if (grade > ReadingGrade.MaximumGrade)
            throw new ArgumentException($"The headline and the sentences read at grade {ReadingGrade.Format(grade)} as one passage. The limit is grade {ReadingGrade.Format(ReadingGrade.MaximumGrade)}.");
    }
}
