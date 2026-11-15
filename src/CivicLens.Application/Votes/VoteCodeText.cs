using CivicLens.Core.Review;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Votes;

/// <summary>
/// Writes the wording of a vote record from its cited facts, with no model. The headline names the chamber, what the vote decided, the bill and
/// its title, or the source description of a vote with no bill, and then the result, in the past tense. It holds no count. A headline that would
/// be too long drops the title or the description, never a part of a sentence. The summary is the headline, then the source description word for
/// word when the headline does not hold it.
/// </summary>
public static class VoteCodeText
{
    public static ReviewText Of(VoteSnapshot snapshot, string? billType, int? billNumber)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var facts = snapshot.Facts;
        var chamber = snapshot.Chamber == Chamber.Senate ? "Senate" : "House";
        var result = ResultSentence(snapshot.Kind, billType, VoteFacts.ValueOf(facts, VoteFactPrefixes.Result) ?? "");
        var description = Clean(VoteFacts.ValueOf(facts, VoteFactPrefixes.Description));
        var named = billType is null ? null : BillNumbers.Of(billType, billNumber!.Value);
        var title = Clean(VoteFacts.ValueOf(facts, VoteFactPrefixes.Title));
        var detail = billType is null ? description ?? (snapshot.Kind == VoteKind.Other ? Clean(VoteFacts.QuestionOf(facts)) : null) : null;
        var headline = new[]
        {
            Headline(chamber, snapshot.Kind, title is null ? named : $"{named}, {title}", detail, result),
            Headline(chamber, snapshot.Kind, named, null, result)
        }.First(text => text.Length <= VoteDraftRevision.MaximumHeadlineLength);
        var summary = description is null || headline.Contains(description, StringComparison.Ordinal)
            ? headline
            : $"{headline} {VoteFacts.ValueOf(facts, VoteFactPrefixes.Description)!.Trim()}";
        return new(headline, summary, ReviewTextOrigin.Code, null);
    }

    private static string Headline(string chamber, VoteKind kind, string? bill, string? detail, string result)
    {
        var decided = bill is null ? Without(kind) : With(kind, bill);
        var subject = detail is null ? decided : $"{decided}: {detail}";
        return $"The {chamber} voted on {subject}. {result}";
    }

    private static string With(VoteKind kind, string bill) => kind switch
    {
        VoteKind.Passage => $"passage of {bill}",
        VoteKind.Suspension => $"passing {bill} with limited debate",
        VoteKind.Amendment => $"an amendment to {bill}",
        VoteKind.Nomination => "a nomination",
        VoteKind.Cloture => $"ending debate on {bill}",
        VoteKind.MotionToProceed => $"starting debate on {bill}",
        VoteKind.MotionToRecommit => $"sending {bill} back to committee",
        VoteKind.Rule => $"the rules for debate set by {bill}",
        VoteKind.PreviousQuestion => $"moving {bill} toward a decision",
        VoteKind.VetoOverride => $"overriding the veto of {bill}",
        VoteKind.ConferenceReport => $"the final text of {bill} from both chambers",
        VoteKind.Other => bill,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Vote kind has no headline.")
    };

    private static string Without(VoteKind kind) => kind switch
    {
        VoteKind.Passage => "passage of a measure",
        VoteKind.Suspension => "passing a measure with limited debate",
        VoteKind.Amendment => "an amendment",
        VoteKind.Nomination => "a nomination",
        VoteKind.Cloture => "ending debate",
        VoteKind.MotionToProceed => "starting debate",
        VoteKind.MotionToRecommit => "sending a measure back to committee",
        VoteKind.Rule => "rules for debate",
        VoteKind.PreviousQuestion => "moving a measure toward a decision",
        VoteKind.VetoOverride => "overriding a veto",
        VoteKind.ConferenceReport => "final text from both chambers",
        VoteKind.Other => "a question",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Vote kind has no headline.")
    };

    private static string ResultSentence(VoteKind kind, string? billType, string result)
    {
        var word = PlainVoteWords.ResultWord(result);
        if (word is null) return $"The result was {result.Trim().TrimEnd('.')}.";
        var passed = word is "Passed" or "Confirmed";
        if (kind == VoteKind.Nomination) return passed ? "The nominee was confirmed." : "The nominee was not confirmed.";
        return $"{Subject(kind, billType)} {(passed ? "passed" : "failed")}.";
    }

    private static string Subject(VoteKind kind, string? billType) => kind switch
    {
        VoteKind.Passage => billType is null ? "The measure" : billType.EndsWith("res", StringComparison.OrdinalIgnoreCase) ? "The resolution" : "The bill",
        VoteKind.Amendment => "The amendment",
        VoteKind.Rule => "The rule",
        VoteKind.VetoOverride => "The override",
        VoteKind.ConferenceReport => "The report",
        VoteKind.Other => "The question",
        _ => "The motion"
    };

    // A source value keeps its words. Only the end mark goes, because the headline adds its own period.
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().TrimEnd('.').TrimEnd();
}
