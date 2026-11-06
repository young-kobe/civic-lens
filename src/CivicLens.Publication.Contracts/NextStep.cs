namespace CivicLens.Publication.Contracts;

/// <summary>
/// The one owner of what happens next after a roll call, by vote kind, result, chamber, and bill type. The sentences are code, never a model prediction.
/// The vote record does not say whether a chamber changed a bill, so a step that depends on that says what usually happens and says that it is not certain.
/// A kind in a chamber that does not hold it, an unknown result, an unknown bill type, and the kind Other give no words. Public wording is the owner's choice, so a change is an edit of this table.
/// </summary>
public static class NextStep
{
    private enum BillShape { None, Law, ConcurrentResolution, SimpleResolution }

    private const string NotCertain = " This record does not say which case applies.";

    /// <summary>The next step in plain words, or null when the inputs give no known step. It never guesses.</summary>
    public static string? Of(PublishedVoteKind kind, string result, PublishedChamber chamber, string? billType)
    {
        if (Succeeded(result) is not { } succeeded) return null;
        return kind switch
        {
            PublishedVoteKind.Rule when chamber == PublishedChamber.House => succeeded ? RuleAdopted : RuleRefused,
            PublishedVoteKind.PreviousQuestion when chamber == PublishedChamber.House => succeeded ? PreviousQuestionAgreed : PreviousQuestionRefused,
            PublishedVoteKind.MotionToRecommit when chamber == PublishedChamber.House => succeeded ? RecommitAgreed : RecommitRefused,
            PublishedVoteKind.Amendment => succeeded ? AmendmentAdopted(chamber) : AmendmentRefused,
            PublishedVoteKind.Suspension when chamber == PublishedChamber.House => succeeded ? Passed(chamber, billType) : SuspensionFailed,
            PublishedVoteKind.Passage => succeeded ? Passed(chamber, billType) : PassageFailed(chamber),
            PublishedVoteKind.ConferenceReport => succeeded ? ConferenceReportAccepted(chamber) : ConferenceReportRejected(chamber),
            PublishedVoteKind.VetoOverride => succeeded ? VetoOverridden(chamber) : VetoStands,
            PublishedVoteKind.Nomination when chamber == PublishedChamber.Senate => succeeded ? NomineeConfirmed : NomineeNotConfirmed,
            PublishedVoteKind.Cloture when chamber == PublishedChamber.Senate => succeeded ? ClotureAgreed : ClotureRefused,
            PublishedVoteKind.MotionToProceed when chamber == PublishedChamber.Senate => succeeded ? ProceedAgreed : ProceedRefused,
            _ => null
        };
    }

    /// <summary>True for a result that passed or confirmed, false for one that failed or did not confirm, null for any other result.</summary>
    private static bool? Succeeded(string result) => PlainVoteWords.ResultWord(result) switch
    {
        "Passed" or "Confirmed" => true,
        "Failed" or "Not confirmed" => false,
        _ => null
    };

    private static string? Passed(PublishedChamber chamber, string? billType) => ShapeOf(billType) switch
    {
        BillShape.Law => OriginOf(billType!) == chamber ? LawPassedInOrigin(chamber) : LawPassedInOther(chamber),
        BillShape.ConcurrentResolution => OriginOf(billType!) == chamber ? ConcurrentPassedInOrigin(chamber) : ConcurrentPassedInOther(chamber),
        BillShape.SimpleResolution => SimplePassed(chamber),
        _ => null
    };

    private static BillShape ShapeOf(string? billType) => billType?.ToLowerInvariant() switch
    {
        "hr" or "s" or "hjres" or "sjres" => BillShape.Law,
        "hconres" or "sconres" => BillShape.ConcurrentResolution,
        "hres" or "sres" => BillShape.SimpleResolution,
        _ => BillShape.None
    };

    private static PublishedChamber OriginOf(string billType) =>
        char.ToLowerInvariant(billType[0]) == 'h' ? PublishedChamber.House : PublishedChamber.Senate;

    private static string Name(PublishedChamber chamber) => chamber == PublishedChamber.House ? "House" : "Senate";

    private static string Other(PublishedChamber chamber) => Name(chamber == PublishedChamber.House ? PublishedChamber.Senate : PublishedChamber.House);

    private const string RuleAdopted = "The House can now debate the bill under these rules. A vote on the bill usually follows.";
    private const string RuleRefused = "The bill cannot move to debate under this rule. House leaders may try again with a new rule.";
    private const string PreviousQuestionAgreed = "Debate is over. The House now votes on the matter that was being debated.";
    private const string PreviousQuestionRefused = "Debate can go on. Members may still try to change the matter.";
    private const string RecommitAgreed = "The bill goes back to a committee. It usually goes no further.";
    private const string RecommitRefused = "The bill stays before the House. A vote on passing it usually follows.";
    private const string AmendmentRefused = "The text stays as it was. The vote on the bill itself comes later.";
    private const string SuspensionFailed = "The House did not pass it by this fast route. It can come back to the floor under the normal rules.";
    private const string VetoStands = "The veto stands. The bill does not become law.";
    private const string NomineeConfirmed = "The nominee is confirmed and can now take the office.";
    private const string NomineeNotConfirmed = "The nominee is not confirmed and does not take the office. The President may name someone else.";
    private const string ClotureAgreed = "Debate is now limited. The Senate then votes on the matter itself.";
    private const string ClotureRefused = "Debate can go on. The matter stays before the Senate and is not voted on yet.";
    private const string ProceedAgreed = "The Senate starts debate on the matter. A vote to end debate and a vote on the matter itself usually come later.";
    private const string ProceedRefused = "The Senate does not start debate on the matter now. It may try again later.";

    private static string AmendmentAdopted(PublishedChamber chamber) =>
        $"The change is now part of the text. The {Name(chamber)} then goes on to vote on the bill itself.";

    private static string PassageFailed(PublishedChamber chamber) =>
        $"The {Name(chamber)} did not pass it. It goes no further unless the {Name(chamber)} votes on it again.";

    private static string LawPassedInOrigin(PublishedChamber chamber) =>
        $"The {Name(chamber)} passed it. It usually goes to the {Other(chamber)} next. If this vote agreed to a change from the {Other(chamber)}, it goes to the President instead.{NotCertain}";

    private static string LawPassedInOther(PublishedChamber chamber) =>
        $"Both chambers have now passed it. If the {Name(chamber)} did not change the text, it goes to the President. If the {Name(chamber)} changed the text, it goes back to the {Other(chamber)}.{NotCertain}";

    private static string ConcurrentPassedInOrigin(PublishedChamber chamber) =>
        $"The {Name(chamber)} passed it. It usually goes to the {Other(chamber)} next. A concurrent resolution does not go to the President.{NotCertain}";

    private static string ConcurrentPassedInOther(PublishedChamber chamber) =>
        $"Both chambers have now passed it. If the {Name(chamber)} did not change the text, Congress has adopted it. If the {Name(chamber)} changed the text, it goes back to the {Other(chamber)}. It does not go to the President.{NotCertain}";

    private static string SimplePassed(PublishedChamber chamber) =>
        $"The {Name(chamber)} passed it. A simple resolution needs only one chamber, so it goes no further.";

    private static string ConferenceReportAccepted(PublishedChamber chamber) =>
        $"The {Name(chamber)} accepted the final text. Once the {Other(chamber)} accepts it too, the bill goes to the President. Neither chamber can change the text now.";

    private static string ConferenceReportRejected(PublishedChamber chamber) =>
        $"The {Name(chamber)} did not accept the final text. The bill goes no further until both chambers work out new text.";

    private static string VetoOverridden(PublishedChamber chamber) =>
        $"The {Name(chamber)} voted to override the veto. If the {Other(chamber)} also votes by two thirds to override it, the bill becomes law. If it already did, the bill is now law.{NotCertain}";
}
