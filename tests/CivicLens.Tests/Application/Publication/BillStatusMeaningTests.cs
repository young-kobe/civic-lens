using CivicLens.Application.Publication;

namespace CivicLens.Tests.Application.Publication;

public sealed class BillStatusMeaningTests
{
    [Theory]
    [InlineData("Referred to the House Committee on Ways and Means.", "hr", "In committee: sent to the House Committee on Ways and Means.")]
    [InlineData("Referred to the Senate Committee on Finance.", "s", "In committee: sent to the Senate Committee on Finance.")]
    [InlineData("Referred to the Committee on Banking, Housing, and Urban Affairs.", "s", "In committee: sent to the Committee on Banking, Housing, and Urban Affairs.")]
    [InlineData("Read twice and referred to the Committee on Finance.", "s", "In committee: sent to the Committee on Finance.")]
    [InlineData("Referred to the Committee on Ways and Means, and in addition to the Committees on Energy and Commerce, Rules, for a period to be subsequently determined by the Speaker, in each case for consideration of such provisions as fall within the jurisdiction of the committee concerned.", "hr",
        "In committee: sent to the Committee on Ways and Means, and in addition to the Committees on Energy and Commerce, Rules.")]
    [InlineData("Referred to the Committee on Science, Space, and Technology, and in addition to the Committee on Armed Services, for a period to be subsequently determined by the Speaker, in each case for consideration of such provisions as fall within the jurisdiction of the committee concerned.", "hr",
        "In committee: sent to the Committee on Science, Space, and Technology, and in addition to the Committee on Armed Services.")]
    [InlineData("Referred to the Committee on the Judiciary. (text: CR S1234)", "s", "In committee: sent to the Committee on the Judiciary.")]
    [InlineData("Read twice and referred to the Committee on Finance. (Sponsor introductory remarks on measure: CR S1234-5)", "s", "In committee: sent to the Committee on Finance.")]
    [InlineData("Referred to the House Committee on Ways and Means. (consideration: CR H12)", "hr", "In committee: sent to the House Committee on Ways and Means.")]
    [InlineData("Referred to the Subcommittee on Health. (text: CR H12)", "hr", "In committee: sent to the Subcommittee on Health.")]
    public void ACommitteeReferralNamesTheCommitteeFromTheTextAndTheChamberOnlyWhenTheTextStatesIt(string text, string billType, string expected)
    {
        Assert.Equal(expected, BillStatusMeaning.Of(text, billType));
    }

    [Fact]
    public void ASubcommitteeReferralNamesTheSubcommitteeAndIsNotReadAsACommitteeReferral()
    {
        Assert.Equal("In committee: sent to the Subcommittee on Housing and Insurance.",
            BillStatusMeaning.Of("Referred to the Subcommittee on Housing and Insurance.", "hr"));
    }

    [Theory]
    [InlineData("Received in the Senate.", "hr", "Passed the House. Now in the Senate.")]
    [InlineData("Received in the Senate and Read twice and referred to the Committee on Finance.", "hjres", "Passed the House. Now in the Senate.")]
    [InlineData("Received in the House.", "s", "Passed the Senate. Now in the House.")]
    [InlineData("Received in the Senate. Read twice. Placed on Senate Legislative Calendar under General Orders. Calendar No. 70.", "hr", "Passed the House. Now in the Senate.")]
    [InlineData("Received in the House.", "sjres", "Passed the Senate. Now in the House.")]
    public void AReceivedActionNamesTheChamberThatPassedTheBillOnlyWhenTheBillTypeSaysWhereItStarted(string text, string billType, string expected)
    {
        Assert.Equal(expected, BillStatusMeaning.Of(text, billType));
    }

    [Theory]
    [InlineData("Received in the Senate.", "s")]
    [InlineData("Received in the House.", "hr")]
    [InlineData("Received in the Senate, read the third time, and passed.", "hr")]
    [InlineData("Received in the House and Held at the desk.", "sjres")]
    [InlineData("Received in the Senate and Placed on the calendar.", "hr")]
    public void AReceivedActionOfTheOtherBillTypeOrAnotherShapeKeepsItsOwnWordsBecauseNoPassageIsKnown(string text, string billType)
    {
        Assert.Equal("Latest official action: " + text, BillStatusMeaning.Of(text, billType));
    }

    [Theory]
    [InlineData("Placed on Senate Legislative Calendar under General Orders. Calendar No. 123.", "Waiting for a possible Senate floor vote.")]
    [InlineData("Placed on the Union Calendar, Calendar No. 12.", "Waiting for a possible House floor vote.")]
    [InlineData("Placed on the House Calendar, Calendar No. 4.", "Waiting for a possible House floor vote.")]
    public void ACalendarActionSaysThatAFloorVoteIsPossibleAndNeverThatItWillHappen(string text, string expected)
    {
        Assert.Equal(expected, BillStatusMeaning.Of(text, "hr"));
    }

    [Theory]
    [InlineData("Ordered to be Reported (Amended) by the Yeas and Nays: 25 - 20.", "hr", "A committee voted to send this bill to the full chamber.")]
    [InlineData("Ordered to be Reported by Voice Vote.", "s", "A committee voted to send this bill to the full chamber.")]
    [InlineData("Ordered to be Reported in the Nature of a Substitute (Amended) by Unanimous Consent.", "hr", "A committee voted to send this bill to the full chamber.")]
    [InlineData("Ordered to be Reported by Voice Vote.", "hres", "A committee voted to send this measure to the full chamber.")]
    [InlineData("Ordered to be Reported by Voice Vote.", "sconres", "A committee voted to send this measure to the full chamber.")]
    [InlineData("Ordered to be Reported by Voice Vote.", "hjres", "A committee voted to send this measure to the full chamber.")]
    public void AnOrderedToBeReportedActionSaysThatACommitteeVotedToSendItOnAndNamesABillOrAMeasureByType(string text, string billType, string expected)
    {
        Assert.Equal(expected, BillStatusMeaning.Of(text, billType));
    }

    [Theory]
    [InlineData("Ordered to be Reported Unfavorably by the Yeas and Nays: 5 - 3.")]
    [InlineData("Ordered to be Reported Adversely.")]
    [InlineData("Ordered to be Reported without amendment by Voice Vote.")]
    public void AnOrderedToBeReportedActionWithAnEndingThatChangesTheMeaningKeepsItsOwnWords(string text)
    {
        Assert.Equal("Latest official action: " + text, BillStatusMeaning.Of(text, "hr"));
    }

    [Theory]
    [InlineData("Became Public Law No: 119-21.", "Became law: Public Law 119-21.")]
    [InlineData("Became Public Law No: 119-21", "Became law: Public Law 119-21.")]
    public void APublicLawActionNamesTheLawNumber(string text, string expected)
    {
        Assert.Equal(expected, BillStatusMeaning.Of(text, "hr"));
    }

    [Theory]
    [InlineData("Motion to reconsider laid on the table.")]
    [InlineData("Passed House.")]
    [InlineData("The measure was Referred to the Committee on Finance.")]
    [InlineData("Committee action: Referred to the Subcommittee on Housing.")]
    [InlineData("Became Public Law No: 119-21. Signed by the President.")]
    [InlineData("Became Private Law No: 119-2.")]
    [InlineData("Not Placed on the Union Calendar.")]
    [InlineData("Referred to the Committee on ")]
    [InlineData("Referred to the Committee on Intelligence (Permanent Select), for a period to be subsequently determined by the Speaker.")]
    [InlineData("Referred to the Committee on Finance. Reported by Senator Smith.")]
    [InlineData("Referred to the Committee on Finance, and in addition to the Committee on Rules.")]
    [InlineData("Referred to the Committee on Finance. (Referred also to the Committee on Rules)")]
    public void AnyOtherTextKeepsItsOwnWordsSoNothingIsHiddenOrGuessedAndNoRuleMatchesInsideTheText(string text)
    {
        Assert.Equal("Latest official action: " + (text.EndsWith('.') ? text : text + "."), BillStatusMeaning.Of(text, "hr"));
    }

    [Theory]
    [InlineData("Referred to the House Committee on Ways and Means.")]
    [InlineData("Placed on the Union Calendar.")]
    [InlineData("Became Public Law No: 119-21.")]
    [InlineData("Motion to reconsider laid on the table.")]
    public void NoMeaningCallsABillASuccessOrAFailure(string text)
    {
        var meaning = BillStatusMeaning.Of(text, "hr");

        foreach (var word in new[] { "success", "fail", "won", "lost", "defeat", "victory" })
            Assert.DoesNotContain(word, meaning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ARawTextWithNoFinalPeriodGetsOneSoTheActionDateStaysItsOwnSentence()
    {
        Assert.Equal("Latest official action: Held at the desk.", BillStatusMeaning.Of("Held at the desk", "s"));
        Assert.Equal("Latest official action: Held at the desk.", BillStatusMeaning.Of("Held at the desk.", "s"));
    }

    [Fact]
    public void ATextOrBillTypeThatIsBlankIsRefused()
    {
        Assert.Throws<ArgumentException>(() => BillStatusMeaning.Of(" ", "hr"));
        Assert.Throws<ArgumentException>(() => BillStatusMeaning.Of("Passed House.", ""));
    }
}
