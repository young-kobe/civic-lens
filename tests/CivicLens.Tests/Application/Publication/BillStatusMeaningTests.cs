using CivicLens.Application.Publication;

namespace CivicLens.Tests.Application.Publication;

public sealed class BillStatusMeaningTests
{
    [Theory]
    [InlineData("Referred to the House Committee on Ways and Means.", "hr", "In committee: sent to the House Committee on Ways and Means.")]
    [InlineData("Referred to the Senate Committee on Finance.", "s", "In committee: sent to the Senate Committee on Finance.")]
    [InlineData("Referred to the Committee on Banking, Housing, and Urban Affairs.", "s", "In committee: sent to the Committee on Banking, Housing, and Urban Affairs.")]
    [InlineData("Read twice and referred to the Committee on Finance.", "s", "In committee: sent to the Committee on Finance.")]
    [InlineData("Referred to the Committee on Ways and Means, and in addition to the Committees on Energy and Commerce, Rules.", "hr",
        "In committee: sent to the Committee on Ways and Means, and in addition to the Committees on Energy and Commerce, Rules.")]
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
    [InlineData("Received in the House and Held at the desk.", "sjres", "Passed the Senate. Now in the House.")]
    public void AReceivedActionNamesTheChamberThatPassedTheBillOnlyWhenTheBillTypeSaysWhereItStarted(string text, string billType, string expected)
    {
        Assert.Equal(expected, BillStatusMeaning.Of(text, billType));
    }

    [Theory]
    [InlineData("Received in the Senate.", "s")]
    [InlineData("Received in the House.", "hr")]
    public void AReceivedActionOfTheOtherBillTypeKeepsItsOwnWordsBecauseNoPassageIsKnown(string text, string billType)
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

    [Fact]
    public void AnOrderedToBeReportedActionSaysThatACommitteeVotedToSendTheBillOn()
    {
        Assert.Equal("A committee voted to send this bill to the full chamber.",
            BillStatusMeaning.Of("Ordered to be Reported (Amended) by the Yeas and Nays: 25 - 20.", "hr"));
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
    public void AnyOtherTextKeepsItsOwnWordsSoNothingIsHiddenOrGuessedAndNoRuleMatchesInsideTheText(string text)
    {
        Assert.Equal("Latest official action: " + text, BillStatusMeaning.Of(text, "hr"));
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
    public void ATextOrBillTypeThatIsBlankIsRefused()
    {
        Assert.Throws<ArgumentException>(() => BillStatusMeaning.Of(" ", "hr"));
        Assert.Throws<ArgumentException>(() => BillStatusMeaning.Of("Passed House.", ""));
    }
}
