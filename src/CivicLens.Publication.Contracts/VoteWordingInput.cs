namespace CivicLens.Publication.Contracts;

/// <summary>Checks the cited question, result, and measure identity once for the vote wording owners.</summary>
internal sealed record VoteWordingInput(string Question, string? Result, string? BillType, bool KnownQuestion,
    bool PossibleConstitutionalPurpose)
{
    internal const string ConcurrenceWithAmendment = "On Motion to Concur in the Senate Amendment with an Amendment";

    public static bool TryRead(VoteWordingSource source, out VoteWordingInput input)
    {
        input = default!;
        if (source.Facts is null || source.KindCitation is not { } kindCitation ||
            !Enum.IsDefined(source.Kind) || !Enum.IsDefined(source.Chamber) || !Enum.IsDefined(source.Source) || source.Result is null)
            return false;

        var questions = new List<PublishedVoteFact>(1);
        var results = new List<PublishedVoteFact>(1);
        var legislation = new List<PublishedVoteFact>(1);
        foreach (var fact in source.Facts)
        {
            if (fact.Text.StartsWith(PublishedVote.QuestionPrefix, StringComparison.OrdinalIgnoreCase)) questions.Add(fact);
            if (fact.Text.StartsWith(PublishedVote.ResultPrefix, StringComparison.OrdinalIgnoreCase)) results.Add(fact);
            if (fact.Text.StartsWith(PublishedVote.LegislationPrefix, StringComparison.OrdinalIgnoreCase)) legislation.Add(fact);
        }

        if (questions.Count != 1 || !questions[0].Text.StartsWith(PublishedVote.QuestionPrefix, StringComparison.Ordinal) ||
            questions[0].Citation != kindCitation || kindCitation.Quote != questions[0].Text)
            return false;

        var question = questions[0].Text[PublishedVote.QuestionPrefix.Length..];
        if (string.IsNullOrWhiteSpace(question) || question != question.Trim()) return false;

        if (results.Count != 1 || results[0].Text != PublishedVote.ResultPrefix + source.Result ||
            results[0].Citation is not { } resultCitation || resultCitation.Quote != results[0].Text)
            return false;

        if (!TryBillType(source, legislation, out var billType)) return false;
        var result = PlainVoteWords.ResultWord(source.Result);
        if (!HasCompatibleResult(source.Kind, result)) result = null;
        input = new(question, result, billType, QuestionMatches(source.Kind, source.Chamber, question), source.PossibleConstitutionalPurpose);
        return true;
    }

    private static bool HasCompatibleResult(PublishedVoteKind kind, string? result) => kind switch
    {
        PublishedVoteKind.Nomination => result is "Confirmed" or "Not confirmed",
        PublishedVoteKind.Other => true,
        _ => result is "Passed" or "Failed"
    };

    private static bool TryBillType(VoteWordingSource source, IReadOnlyList<PublishedVoteFact> legislation, out string? billType)
    {
        billType = null;
        if ((source.BillType is null) != (source.BillNumber is null)) return false;

        if (legislation.Count > 1) return false;
        if (legislation.Count == 1 && (source.BillType is null || source.BillNumber is not > 0 ||
            legislation[0].Citation?.Quote != legislation[0].Text ||
            !legislation[0].Text.Equals($"{PublishedVote.LegislationPrefix}{source.BillType.ToUpperInvariant()} {source.BillNumber.Value}", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (legislation.Count == 0) return true;

        if (source.BillType is not { } sourceType) return true;
        if (source.BillNumber is not > 0) return false;
        var normalized = sourceType.ToLowerInvariant();
        if (normalized is "hr" or "s" or "hjres" or "sjres" or "hconres" or "sconres" or "hres" or "sres")
            billType = normalized;
        return true;
    }

    public static bool QuestionIs(string question, params string[] knownQuestions) =>
        knownQuestions.Contains(question, StringComparer.OrdinalIgnoreCase);

    public static bool QuestionMatches(PublishedVoteKind kind, PublishedChamber chamber, string question)
    {
        return (kind, chamber) switch
        {
            (PublishedVoteKind.Rule, PublishedChamber.House) => QuestionIs(question, "On Agreeing to the Resolution"),
            (PublishedVoteKind.PreviousQuestion, PublishedChamber.House) => QuestionIs(question, "On Ordering the Previous Question"),
            (PublishedVoteKind.MotionToRecommit, PublishedChamber.House) => QuestionIs(question, "On Motion to Recommit", "On Motion to Commit",
                "On Motion to Commit with Instructions", "On Motion to Recommit with Instructions"),
            (PublishedVoteKind.Amendment, PublishedChamber.House) => QuestionIs(question, "On Agreeing to the Amendment", "On Agreeing to the Amendment, as Modified"),
            (PublishedVoteKind.Amendment, PublishedChamber.Senate) => QuestionIs(question, "On the Amendment"),
            (PublishedVoteKind.Suspension, PublishedChamber.House) => QuestionIs(question, "On Motion to Suspend the Rules and Pass",
                "On Motion to Suspend the Rules and Pass, as Amended", "On Motion to Suspend the Rules and Concur in the Senate Amendments"),
            (PublishedVoteKind.Passage, PublishedChamber.House) => QuestionIs(question, "On Passage",
                "On Motion to Concur in the Senate Amendment", "On Motion to Concur in the Senate Amendments",
                "On Agreeing to the Resolution", ConcurrenceWithAmendment),
            (PublishedVoteKind.Passage, PublishedChamber.Senate) => QuestionIs(question, "On Passage of the Bill",
                "On the Joint Resolution", "On the Concurrent Resolution", "On the Resolution"),
            (PublishedVoteKind.ConferenceReport, PublishedChamber.House) => QuestionIs(question, "On Agreeing to the Conference Report"),
            (PublishedVoteKind.ConferenceReport, PublishedChamber.Senate) => QuestionIs(question, "On the Conference Report"),
            (PublishedVoteKind.VetoOverride, PublishedChamber.House) => QuestionIs(question, "Passage, Objections of the President To The Contrary Notwithstanding"),
            (PublishedVoteKind.VetoOverride, PublishedChamber.Senate) => QuestionIs(question, "On Overriding the Veto"),
            (PublishedVoteKind.Nomination, PublishedChamber.Senate) => QuestionIs(question, "On the Nomination"),
            (PublishedVoteKind.Cloture, PublishedChamber.Senate) => QuestionIs(question, "On the Cloture Motion", "On Cloture on the Motion to Proceed"),
            (PublishedVoteKind.MotionToProceed, PublishedChamber.Senate) => QuestionIs(question, "On the Motion to Proceed"),
            _ => false
        };
    }

}
