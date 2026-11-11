namespace CivicLens.Publication.Contracts;

/// <summary>Owns the approved next-step wording supported by a vote's cited question, result, and measure identity.</summary>
public static class NextStep
{
    public const string Heading = "What happens next";
    public const string NotShown = "This record does not show what happens next.";

    private const string RulePassed = "The House can consider the measure under these rules. This record does not show which vote comes next.";
    private const string RuleFailed = "This proposed rule does not authorize consideration of the measure. This record does not show how the House will proceed.";
    private const string PreviousQuestionPassed = "The order moves the House toward votes on the covered questions. Some debate or motions may still be allowed.";
    private const string PreviousQuestionFailed = "This motion does not close debate or changes. Further proceedings follow the House rules.";
    private const string RecommitPassed = "The measure is sent to committee under the terms of the motion. This record does not show the committee's next action.";
    private const string RecommitFailed = "This motion did not send the measure to committee. The House can continue proceedings on the matter.";
    private const string AmendmentPassed = "The approved change becomes part of the pending text. This vote does not by itself pass the underlying measure.";
    private const string AmendmentFailed = "The pending text does not get this proposed change. The chamber can continue work on the underlying matter.";
    private const string SuspensionFailed = "The House can take up the matter by another allowed procedure. This record does not schedule a new vote.";
    private const string PassagePassed = "The next step depends on the measure and whether both chambers have agreed to the same text. This record does not establish that step.";
    private const string ConferencePassed = "The next step depends on the other chamber's action and any matters left in dispute. This record does not establish that step.";
    private const string VetoOverrideFailed = "An override needs the required votes in both chambers. This record does not show a later attempt.";
    private const string CloturePassed = "Debate is now limited, usually to 30 more hours, and many nominations get 2 hours. The Senate can agree to extend that time. This vote does not decide the question itself, such as a motion, a bill, or a nomination.";
    private const string ClotureFailed = "This motion does not impose its proposed debate limit. This record does not show the Senate's next step.";
    private const string ProceedPassed = "The Senate can take up the matter for consideration. This vote does not decide the matter itself.";
    private const string ProceedFailed = "This motion does not advance consideration of the matter. This record does not show the Senate's next business.";
    private const string OrdinaryBillPassed = "Both chambers must approve the same text before this bill can go to the President. This record does not show its next destination.";
    private const string SimpleResolutionPassed = "This resolution needs no approval from the other chamber or the President.";
    private const string ConcurrentResolutionPassed = "Both chambers must agree to this resolution. It does not go to the President.";
    private const string JointResolutionPassed = "Both chambers must agree to this resolution. This record does not establish the next step.";
    private const string ConcurrenceWithAmendmentPassed = "The new change needs agreement from the other chamber. This vote does not settle the chambers' differences.";
    private const string BillConferencePassed = "Both chambers must agree to the same text before this bill can go to the President. This record does not show whether all differences are resolved.";
    private const string FirstOverridePassed = "The measure goes to the other chamber. Two thirds of those voting Yes or No there must also override the veto. That chamber needs a quorum, enough members present to conduct business.";
    private const string FinalOverridePassed = "The veto is overridden, and the measure becomes law.";
    private const string NominationPassed = "Confirmation gives the President Senate consent to make the appointment. It does not show whether or when the nominee takes office.";
    private const string NominationFailed = "This vote does not give Senate consent for the appointment. This record does not show another nomination or vote.";

    internal static string For(PublishedVoteKind kind, PublishedChamber chamber, VoteWordingInput input)
    {
        if (input.Result is null || !input.KnownQuestion) return NotShown;

        return kind switch
        {
            PublishedVoteKind.Rule => Result(input, RulePassed, RuleFailed),
            PublishedVoteKind.PreviousQuestion => Result(input, PreviousQuestionPassed, PreviousQuestionFailed),
            PublishedVoteKind.MotionToRecommit => Result(input, RecommitPassed, RecommitFailed),
            PublishedVoteKind.Amendment => Result(input, AmendmentPassed, AmendmentFailed),
            PublishedVoteKind.Suspension when input.Result == "Failed" => SuspensionFailed,
            PublishedVoteKind.Suspension when input.Result == "Passed" => SuspensionPassedFor(chamber, input),
            PublishedVoteKind.Passage when input.Result == "Passed" => PassagePassedFor(chamber, input.Question, input.BillType),
            PublishedVoteKind.ConferenceReport when input.Result == "Passed" => input.BillType is "hr" or "s" ? BillConferencePassed : ConferencePassed,
            PublishedVoteKind.VetoOverride when input.Result == "Failed" => VetoOverrideFailed,
            PublishedVoteKind.VetoOverride when input.Result == "Passed" => OverridePassedFor(chamber, input),
            PublishedVoteKind.Nomination when input.Result == "Confirmed" => NominationPassed,
            PublishedVoteKind.Nomination when input.Result == "Not confirmed" => NominationFailed,
            PublishedVoteKind.Cloture => Result(input, CloturePassed, ClotureFailed),
            PublishedVoteKind.MotionToProceed => Result(input, ProceedPassed, ProceedFailed),
            _ => NotShown
        };
    }

    private static string Result(VoteWordingInput input, string passed, string failed) => input.Result switch
    {
        "Passed" => passed,
        "Failed" => failed,
        _ => NotShown
    };

    private static string PassagePassedFor(PublishedChamber chamber, string question, string? billType)
    {
        if (question.Equals(VoteWordingInput.ConcurrenceWithAmendment, StringComparison.OrdinalIgnoreCase))
            return ConcurrenceWithAmendmentPassed;
        if (billType is null) return PassagePassed;

        if ((billType is "hr" or "s") && VoteWordingInput.QuestionIs(question, "On Passage", "On Passage of the Bill")) return OrdinaryBillPassed;
        if (billType == (chamber == PublishedChamber.House ? "hres" : "sres") &&
            VoteWordingInput.QuestionIs(question, "On Agreeing to the Resolution", "On the Resolution")) return SimpleResolutionPassed;
        if ((billType is "hconres" or "sconres") &&
            VoteWordingInput.QuestionIs(question, "On Agreeing to the Resolution", "On the Concurrent Resolution")) return ConcurrentResolutionPassed;
        if ((billType is "hjres" or "sjres") &&
            VoteWordingInput.QuestionIs(question, "On Passage", "On Passage of the Bill", "On Agreeing to the Resolution", "On the Joint Resolution"))
            return JointResolutionPassed;
        return PassagePassed;
    }

    private static string SuspensionPassedFor(PublishedChamber chamber, VoteWordingInput input) =>
        VoteWordingInput.QuestionIs(input.Question, "On Motion to Suspend the Rules and Pass", "On Motion to Suspend the Rules and Pass, as Amended")
            ? PassagePassedFor(chamber, "On Passage", input.BillType)
            : PassagePassed;

    private static string OverridePassedFor(PublishedChamber chamber, VoteWordingInput input)
    {
        if (input.BillType is not ("hr" or "s" or "hjres" or "sjres") || input.PossibleConstitutionalPurpose) return NotShown;
        var houseOrigin = input.BillType is "hr" or "hjres";
        return houseOrigin == (chamber == PublishedChamber.House) ? FirstOverridePassed : FinalOverridePassed;
    }
}
