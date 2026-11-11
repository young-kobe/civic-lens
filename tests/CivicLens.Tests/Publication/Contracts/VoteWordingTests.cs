using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class VoteWordingTests
{
    private const string YesFallback = "A Yes vote supported the question before the chamber.";
    private const string NoFallback = "A No vote opposed the question before the chamber.";
    private const string UnknownNextStep = "This record does not show what happens next.";

    [Theory]
    [InlineData(PublishedVoteKind.Rule, "A Yes vote supported the rules for considering the measure.", "A No vote opposed the rules for considering the measure.")]
    [InlineData(PublishedVoteKind.PreviousQuestion, "A Yes vote supported moving the matter toward a vote, with limits on further debate and changes.", "A No vote opposed moving the matter toward a vote this way.")]
    [InlineData(PublishedVoteKind.MotionToRecommit, "A Yes vote supported sending the measure to a committee.", "A No vote opposed sending the measure to a committee.")]
    [InlineData(PublishedVoteKind.Amendment, "A Yes vote supported the proposed change.", "A No vote opposed the proposed change.")]
    [InlineData(PublishedVoteKind.Suspension, "A Yes vote supported the action named in the question under a procedure with limited debate.", "A No vote opposed the action named in the question under a procedure with limited debate.")]
    [InlineData(PublishedVoteKind.Passage, "A Yes vote supported passing or agreeing to the matter named in the question.", "A No vote opposed passing or agreeing to the matter named in the question.")]
    [InlineData(PublishedVoteKind.ConferenceReport, "A Yes vote supported the proposal from members of both chambers on how to resolve their differences.", "A No vote opposed the proposal from members of both chambers on how to resolve their differences.")]
    [InlineData(PublishedVoteKind.VetoOverride, "A Yes vote supported reversing the President's decision to reject the bill in this chamber.", "A No vote opposed reversing the President's decision to reject the bill in this chamber.")]
    [InlineData(PublishedVoteKind.Cloture, "A Yes vote supported limiting further debate on the question before the Senate.", "A No vote opposed limiting further debate on the question before the Senate.")]
    [InlineData(PublishedVoteKind.MotionToProceed, "A Yes vote supported beginning consideration of the matter named in the question.", "A No vote opposed beginning consideration of the matter named in the question.")]
    public void ARecognizedQuestionUsesItsApprovedYesAndNoWords(PublishedVoteKind kind, string yes, string no)
    {
        var question = KnownQuestion(kind);

        var chamber = kind is PublishedVoteKind.Cloture or PublishedVoteKind.MotionToProceed ? PublishedChamber.Senate : PublishedChamber.House;
        var wording = VoteWording.Of(Vote(kind, question, chamber: chamber))!.Value;

        Assert.Equal((yes, no), (wording.Yes, wording.No));
    }

    [Fact]
    public void TheCitedPartsOfAVoteGiveTheSameWordsAsThePublishedVote()
    {
        var vote = Vote(PublishedVoteKind.Passage, "On Passage");

        Assert.Equal(VoteWording.Of(vote), VoteWording.Of(VoteWordingSource.Of(vote)));
        Assert.NotNull(VoteWording.Of(VoteWordingSource.Of(vote)));
    }

    [Fact]
    public void CitedPartsWithoutAQuestionCitationGiveNoWording()
    {
        var source = VoteWordingSource.Of(Vote(PublishedVoteKind.Passage, "On Passage")) with { KindCitation = null };

        Assert.Null(VoteWording.Of(source));
    }

    [Fact]
    public void ARecognizedQuestionMatchesWithoutCaseSensitivity()
    {
        var vote = Vote(PublishedVoteKind.Passage, "on passage");

        var wording = VoteWording.Of(vote)!.Value;

        Assert.Equal("A Yes vote supported passing or agreeing to the matter named in the question.", wording.Yes);
        Assert.Equal("The next step depends on the measure and whether both chambers have agreed to the same text. This record does not establish that step.", wording.NextStep);
    }

    [Fact]
    public void ANominationWithoutAConfirmationResultUsesTheGeneralMeaning()
    {
        var vote = Vote(PublishedVoteKind.Nomination, "On the Nomination", chamber: PublishedChamber.Senate);

        var wording = VoteWording.Of(vote)!.Value;

        Assert.Equal((YesFallback, NoFallback), (wording.Yes, wording.No));
    }

    [Fact]
    public void TheCloturePassedWordingUsesTheCitedVoteviewQuestionAndRecognizedResult()
    {
        var vote = Vote(PublishedVoteKind.Cloture, "On the Cloture Motion", "Cloture Motion Agreed to", PublishedChamber.Senate);

        Assert.Equal("Debate is now limited, usually to 30 more hours, and many nominations get 2 hours. The Senate can agree to extend that time. This vote does not decide the question itself, such as a motion, a bill, or a nomination.", VoteWording.Of(vote)!.Value.NextStep);
    }

    [Theory]
    [InlineData(PublishedVoteKind.Passage, "On Passage", "hr", "HR 7", PublishedChamber.House, PublishedVoteSource.CongressGov, "Both chambers must approve the same text before this bill can go to the President. This record does not show its next destination.")]
    [InlineData(PublishedVoteKind.Passage, "On Agreeing to the Resolution", "hres", "HRES 7", PublishedChamber.House, PublishedVoteSource.CongressGov, "This resolution needs no approval from the other chamber or the President.")]
    [InlineData(PublishedVoteKind.Passage, "On the Concurrent Resolution", "sconres", null, PublishedChamber.Senate, PublishedVoteSource.Voteview, "The next step depends on the measure and whether both chambers have agreed to the same text. This record does not establish that step.")]
    [InlineData(PublishedVoteKind.Passage, "On Agreeing to the Resolution", "sconres", "SCONRES 7", PublishedChamber.House, PublishedVoteSource.CongressGov, "Both chambers must agree to this resolution. It does not go to the President.")]
    [InlineData(PublishedVoteKind.Passage, "On Passage", "hjres", "HJRES 7", PublishedChamber.House, PublishedVoteSource.CongressGov, "Both chambers must agree to this resolution. This record does not establish the next step.")]
    public void APassageStepUsesOnlyACompleteQuestionAndMatchingMeasureIdentity(PublishedVoteKind kind, string question, string billType,
        string? legislation, PublishedChamber chamber, PublishedVoteSource source, string expected)
    {
        var vote = Vote(kind, question, chamber: chamber, billType: billType, legislation: legislation, source: source);

        Assert.Equal(expected, VoteWording.Of(vote)!.Value.NextStep);
    }

    [Fact]
    public void AHouseRendererQuestionAndResultUseTheGeneralPassageStepWhenItsMeasureIdentityDoesNotFitASpecialRoute()
    {
        var vote = Vote(PublishedVoteKind.Passage, "On Motion to Concur in the Senate Amendment", billType: "hconres", legislation: "HCONRES 14") with { BillNumber = 14 };

        Assert.Equal("The next step depends on the measure and whether both chambers have agreed to the same text. This record does not establish that step.", VoteWording.Of(vote)!.Value.NextStep);
    }

    [Theory]
    [InlineData(PublishedVoteKind.Rule, "On Agreeing to the Resolution", "Passed", PublishedChamber.House, "The House can consider the measure under these rules. This record does not show which vote comes next.")]
    [InlineData(PublishedVoteKind.Rule, "On Agreeing to the Resolution", "Failed", PublishedChamber.House, "This proposed rule does not authorize consideration of the measure. This record does not show how the House will proceed.")]
    [InlineData(PublishedVoteKind.PreviousQuestion, "On Ordering the Previous Question", "Passed", PublishedChamber.House, "The order moves the House toward votes on the covered questions. Some debate or motions may still be allowed.")]
    [InlineData(PublishedVoteKind.PreviousQuestion, "On Ordering the Previous Question", "Failed", PublishedChamber.House, "This motion does not close debate or changes. Further proceedings follow the House rules.")]
    [InlineData(PublishedVoteKind.MotionToRecommit, "On Motion to Commit with Instructions", "Passed", PublishedChamber.House, "The measure is sent to committee under the terms of the motion. This record does not show the committee's next action.")]
    [InlineData(PublishedVoteKind.MotionToRecommit, "On Motion to Recommit", "Failed", PublishedChamber.House, "This motion did not send the measure to committee. The House can continue proceedings on the matter.")]
    [InlineData(PublishedVoteKind.Amendment, "On Agreeing to the Amendment", "Passed", PublishedChamber.House, "The approved change becomes part of the pending text. This vote does not by itself pass the underlying measure.")]
    [InlineData(PublishedVoteKind.Amendment, "On Agreeing to the Amendment, as Modified", "Failed", PublishedChamber.House, "The pending text does not get this proposed change. The chamber can continue work on the underlying matter.")]
    [InlineData(PublishedVoteKind.Suspension, "On Motion to Suspend the Rules and Pass", "Failed", PublishedChamber.House, "The House can take up the matter by another allowed procedure. This record does not schedule a new vote.")]
    [InlineData(PublishedVoteKind.ConferenceReport, "On Agreeing to the Conference Report", "Passed", PublishedChamber.House, "The next step depends on the other chamber's action and any matters left in dispute. This record does not establish that step.")]
    [InlineData(PublishedVoteKind.VetoOverride, "Passage, Objections of the President To The Contrary Notwithstanding", "Failed", PublishedChamber.House, "An override needs the required votes in both chambers. This record does not show a later attempt.")]
    [InlineData(PublishedVoteKind.Cloture, "On the Cloture Motion", "Failed", PublishedChamber.Senate, "This motion does not impose its proposed debate limit. This record does not show the Senate's next step.")]
    [InlineData(PublishedVoteKind.MotionToProceed, "On the Motion to Proceed", "Passed", PublishedChamber.Senate, "The Senate can take up the matter for consideration. This vote does not decide the matter itself.")]
    [InlineData(PublishedVoteKind.MotionToProceed, "On the Motion to Proceed", "Failed", PublishedChamber.Senate, "This motion does not advance consideration of the matter. This record does not show the Senate's next business.")]
    public void ARecognizedQuestionAndResultUseTheApprovedNextStep(PublishedVoteKind kind, string question, string result,
        PublishedChamber chamber, string expected)
    {
        var vote = Vote(kind, question, result, chamber);

        Assert.Equal(expected, VoteWording.Of(vote)!.Value.NextStep);
    }

    [Theory]
    [InlineData("On Passage, extra words")]
    [InlineData("Question mentions On Passage in a longer question")]
    [InlineData("Earlier business, then On Passage")]
    public void AnUnrecognizedQuestionSuffixKeepsOnlyTheApprovedGeneralMeaningAndUnknownStep(string question)
    {
        var vote = Vote(PublishedVoteKind.Passage, question);

        var wording = VoteWording.Of(vote)!.Value;

        Assert.Equal((YesFallback, NoFallback), (wording.Yes, wording.No));
        Assert.Equal(UnknownNextStep, wording.NextStep);
    }

    [Fact]
    public void AQuestionKindMismatchUsesFallbackWords()
    {
        var vote = Vote(PublishedVoteKind.Cloture, "On Passage", chamber: PublishedChamber.Senate);

        var wording = VoteWording.Of(vote)!.Value;

        Assert.Equal((YesFallback, NoFallback), (wording.Yes, wording.No));
        Assert.Equal(UnknownNextStep, wording.NextStep);
    }

    [Theory]
    [InlineData(PublishedVoteKind.Passage, "On Passage", "Confirmed", PublishedChamber.House)]
    [InlineData(PublishedVoteKind.Nomination, "On the Nomination", "Passed", PublishedChamber.Senate)]
    [InlineData(PublishedVoteKind.Nomination, "On the Nomination", "Failed", PublishedChamber.Senate)]
    public void AnIncompatibleResultKindUsesOnlyTheFallbacks(PublishedVoteKind kind, string question, string result, PublishedChamber chamber)
    {
        var wording = VoteWording.Of(Vote(kind, question, result, chamber))!.Value;

        Assert.Equal((YesFallback, NoFallback, UnknownNextStep), (wording.Yes, wording.No, wording.NextStep));
    }

    [Fact]
    public void AResolutionIdentityFromTheOtherChamberUsesTheGeneralStep()
    {
        var vote = Vote(PublishedVoteKind.Passage, "On the Resolution", chamber: PublishedChamber.Senate,
            billType: "hres", legislation: "HRES 7");

        Assert.Equal("The next step depends on the measure and whether both chambers have agreed to the same text. This record does not establish that step.",
            VoteWording.Of(vote)!.Value.NextStep);
    }

    [Fact]
    public void MissingOrUnknownResultAndConflictingCapturedFactsUseFallbacks()
    {
        var baseline = Vote(PublishedVoteKind.Passage, "On Passage");
        var unknownResult = Vote(PublishedVoteKind.Passage, "On Passage", "Point of Order Sustained");
        var missingResult = baseline with { Facts = [.. baseline.Facts.Where(fact => !fact.Text.StartsWith(PublishedVote.ResultPrefix, StringComparison.Ordinal))] };
        var conflictingResult = Vote(PublishedVoteKind.Passage, "On Passage", extraFacts: ["Result: Failed"]);
        var conflictingQuestion = Vote(PublishedVoteKind.Passage, "On Passage", extraFacts: ["Question: On Passage with an Amendment"]);

        var unknownWording = VoteWording.Of(unknownResult)!.Value;
        Assert.Equal((YesFallback, NoFallback), (unknownWording.Yes, unknownWording.No));
        Assert.Equal(UnknownNextStep, unknownWording.NextStep);
        Assert.Null(VoteWording.Of(missingResult));
        Assert.Null(VoteWording.Of(conflictingResult));
        Assert.Null(VoteWording.Of(conflictingQuestion));
    }

    [Fact]
    public void AConflictingCitedMeasureIdentityDoesNotSelectAMeasureSpecificStep()
    {
        var vote = Vote(PublishedVoteKind.Passage, "On Passage", billType: "hr", legislation: "S 7");

        Assert.Null(VoteWording.Of(vote));
    }

    [Fact]
    public void UncitedVoteviewBillMetadataCannotSelectAMeasureSpecificStep()
    {
        var vote = Vote(PublishedVoteKind.Passage, "On Passage of the Bill", chamber: PublishedChamber.Senate,
            source: PublishedVoteSource.Voteview);
        var changedMetadata = vote with { BillType = "hr", BillNumber = 7 };

        Assert.Equal(VoteWording.Of(vote), VoteWording.Of(changedMetadata));
        Assert.Equal("The next step depends on the measure and whether both chambers have agreed to the same text. This record does not establish that step.",
            VoteWording.Of(changedMetadata)!.Value.NextStep);
    }

    [Theory]
    [InlineData("Nomination Confirmed", "Confirmation gives the President Senate consent to make the appointment. It does not show whether or when the nominee takes office.")]
    [InlineData("Nomination Rejected", "This vote does not give Senate consent for the appointment. This record does not show another nomination or vote.")]
    public void ACitedNominationOutcomeNeedsNoGuessedOffice(string result, string nextStep)
    {
        var vote = Vote(PublishedVoteKind.Nomination, "On the Nomination", result, PublishedChamber.Senate);
        var wording = VoteWording.Of(vote)!.Value;

        Assert.Equal("A Yes vote supported confirming the nominee.", wording.Yes);
        Assert.Equal("A No vote opposed confirming the nominee.", wording.No);
        Assert.Equal(nextStep, wording.NextStep);
    }

    [Theory]
    [InlineData(PublishedVoteKind.ConferenceReport, "On Agreeing to the Conference Report", "hr", "Both chambers must agree to the same text before this bill can go to the President. This record does not show whether all differences are resolved.")]
    [InlineData(PublishedVoteKind.VetoOverride, "Passage, Objections of the President To The Contrary Notwithstanding", "hr", "The measure goes to the other chamber. Two thirds of those voting Yes or No there must also override the veto. That chamber needs a quorum, enough members present to conduct business.")]
    [InlineData(PublishedVoteKind.VetoOverride, "Passage, Objections of the President To The Contrary Notwithstanding", "s", "The veto is overridden, and the measure becomes law.")]
    [InlineData(PublishedVoteKind.VetoOverride, "Passage, Objections of the President To The Contrary Notwithstanding", "hjres", "The measure goes to the other chamber. Two thirds of those voting Yes or No there must also override the veto. That chamber needs a quorum, enough members present to conduct business.")]
    [InlineData(PublishedVoteKind.Passage, "On Motion to Concur in the Senate Amendment with an Amendment", "hr", "The new change needs agreement from the other chamber. This vote does not settle the chambers' differences.")]
    [InlineData(PublishedVoteKind.Suspension, "On Motion to Suspend the Rules and Pass", "hr", "Both chambers must approve the same text before this bill can go to the President. This record does not show its next destination.")]
    public void ACompleteQuestionAndCitedIdentityUseTheApprovedSpecificRoute(PublishedVoteKind kind, string question, string billType, string expected)
    {
        var vote = Vote(kind, question, billType: billType, legislation: $"{billType.ToUpperInvariant()} 7");

        Assert.Equal(expected, VoteWording.Of(vote)!.Value.NextStep);
    }

    [Fact]
    public void APossibleConstitutionalPurposeCannotSupportAnOverrideLawRoute()
    {
        var vote = Vote(PublishedVoteKind.VetoOverride, "Passage, Objections of the President To The Contrary Notwithstanding",
            billType: "hjres", legislation: "HJRES 7",
            extraFacts: ["Title: Proposing an amendment to the Constitution of the United States to change the terms of office."]);

        Assert.Equal(UnknownNextStep, VoteWording.Of(vote)!.Value.NextStep);
    }

    [Fact]
    public void TheSharedKindLabelDoesNotPromiseAnImmediateVote()
    {
        Assert.Equal("Vote to move toward a decision", PlainVoteWords.Kind(PublishedVoteKind.PreviousQuestion));
    }

    private static string KnownQuestion(PublishedVoteKind kind) => kind switch
    {
        PublishedVoteKind.Rule => "On Agreeing to the Resolution",
        PublishedVoteKind.Passage => "On Passage",
        PublishedVoteKind.PreviousQuestion => "On Ordering the Previous Question",
        PublishedVoteKind.MotionToRecommit => "On Motion to Commit with Instructions",
        PublishedVoteKind.Amendment => "On Agreeing to the Amendment",
        PublishedVoteKind.Suspension => "On Motion to Suspend the Rules and Pass",
        PublishedVoteKind.ConferenceReport => "On Agreeing to the Conference Report",
        PublishedVoteKind.VetoOverride => "Passage, Objections of the President To The Contrary Notwithstanding",
        PublishedVoteKind.Nomination => "On the Nomination",
        PublishedVoteKind.Cloture => "On the Cloture Motion",
        PublishedVoteKind.MotionToProceed => "On the Motion to Proceed",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static PublishedVote Vote(PublishedVoteKind kind, string question, string result = "Passed",
        PublishedChamber chamber = PublishedChamber.House, string? billType = null, string? legislation = null, string[]? extraFacts = null,
        PublishedVoteSource source = PublishedVoteSource.CongressGov)
    {
        const string detailId = "detail";
        var questionLine = $"Question: {question}";
        var resultLine = $"Result: {result}";
        var factLines = new List<string>
        {
            "Started: 2025-04-10", questionLine, resultLine
        };
        if (legislation is not null) factLines.Add($"Legislation: {legislation}");
        if (extraFacts is not null) factLines.AddRange(extraFacts);
        var detailText = string.Join('\n', factLines);
        var facts = factLines.Select(Fact).ToArray();

        return new()
        {
            Corrections = [],
            RecordId = new string('5', 64),
            RevisionNumber = 1,
            Headline = "Headline",
            Summary = "Summary",
            TextOrigin = PublishedTextOrigin.Model,
            Chamber = chamber,
            Congress = 119,
            Session = 1,
            RollCallNumber = 100,
            Source = source,
            Kind = kind,
            KindCitation = Cite(questionLine),
            VotedOn = new DateOnly(2025, 4, 10),
            Result = result,
            Totals = new() { Yea = 1, Nay = 1, Present = 0, NotVoting = 0 },
            PartyMajorities = [PublishedPartyMajority.Of("Democrat", 0, 1), PublishedPartyMajority.Of("Republican", 1, 0)],
            VotedOnCitation = Cite("Started: 2025-04-10"),
            Facts = [.. facts],
            Members = [],
            BillType = billType,
            BillNumber = billType is null ? null : 7,
            TrackedIssueIds = [],
            Sources = [new() { ExtractionId = detailId, Url = "https://example.test/vote", ObservedAtUtc = DateTimeOffset.UnixEpoch, Text = detailText }],
            ApprovedAtUtc = DateTimeOffset.UnixEpoch,
            FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
        };

        PublishedVoteFact Fact(string text) => new() { Text = text, Citation = Cite(text) };
        PublishedCitation Cite(string quote) => new() { ExtractionId = detailId, Start = detailText.IndexOf(quote, StringComparison.Ordinal), Length = quote.Length, Quote = quote };
    }
}
