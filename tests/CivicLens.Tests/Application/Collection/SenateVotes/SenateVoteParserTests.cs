using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.SenateVotes;

public sealed class SenateVoteParserTests
{
    [Fact]
    public void ModifyDateIsReadAsUsEasternBecauseTheFileGivesNoZoneAndTheRevisionKeyNeedsAnExactInstant()
    {
        var detail = Parse(SenateVoteFixtures.Vote());

        Assert.Equal(new DateTimeOffset(2025, 1, 10, 14, 57, 0, TimeSpan.FromHours(-5)), detail.Vote.ModifyDate);
        Assert.Equal(new DateTimeOffset(2025, 1, 9, 14, 54, 0, TimeSpan.FromHours(-5)), detail.VoteDate);
    }

    [Fact]
    public void SummerTimesUseTheDaylightOffset()
    {
        var detail = Parse(SenateVoteFixtures.Vote(modifyDate: "July 3, 2025,  09:42 PM"));

        Assert.Equal(TimeSpan.FromHours(-4), detail.Vote.ModifyDate.Offset);
    }

    [Fact]
    public void ABillDocumentGivesTheBillInTheStoredFormSoTheVoteLinksToTheBill()
    {
        var vote = Parse(SenateVoteFixtures.Vote()).Vote;

        Assert.Equal(("s", 5), (vote.BillType, vote.BillNumber));
    }

    [Theory]
    [InlineData("H.R.", "hr")]
    [InlineData("H.J.Res.", "hjres")]
    [InlineData("S.Con.Res.", "sconres")]
    public void SenateCitationTypesMapToStoredBillTypes(string citation, string stored)
    {
        var document = SenateVoteFixtures.DefaultDocument.Replace("<document_type>S.</document_type>", $"<document_type>{citation}</document_type>");

        Assert.Equal(stored, Parse(SenateVoteFixtures.Vote(document: document)).Vote.BillType);
    }

    [Theory]
    [InlineData("PN")]
    [InlineData("S.Amdt.")]
    public void ADocumentThatIsNotABillGivesNoBillLinkButKeepsItsName(string type)
    {
        var document = SenateVoteFixtures.DefaultDocument.Replace("<document_type>S.</document_type>", $"<document_type>{type}</document_type>");

        var detail = Parse(SenateVoteFixtures.Vote(document: document));

        Assert.Equal((null, null), (detail.Vote.BillType, detail.Vote.BillNumber));
        Assert.Equal("S. 5", detail.DocumentName);
    }

    [Fact]
    public void ANominationWithAnIdThatIsNotAWholeNumberStillParsesBecauseOnlyABillNeedsANumber()
    {
        var document = SenateVoteFixtures.DefaultDocument
            .Replace("<document_type>S.</document_type>", "<document_type>PN</document_type>")
            .Replace("<document_number>5</document_number>", "<document_number>12-3</document_number>")
            .Replace("<document_name>S. 5</document_name>", "<document_name>PN12-3</document_name>");

        var detail = Parse(SenateVoteFixtures.Vote(document: document));

        Assert.Equal((null, null, "PN12-3"), (detail.Vote.BillType, detail.Vote.BillNumber, detail.DocumentName));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("5-1")]
    public void ABillWithoutAWholeNumberIsInvalidBecauseTheBillLinkMustBeExact(string number)
    {
        var document = SenateVoteFixtures.DefaultDocument.Replace("<document_number>5</document_number>", $"<document_number>{number}</document_number>");

        Assert.Throws<InvalidDataException>(() => Parse(SenateVoteFixtures.Vote(document: document)));
    }

    [Fact]
    public void ATieBreakVoteParsesBecauseTheVicePresidentIsInNeitherTheCountsNorTheMembers()
    {
        var tieBreaker = "<tie_breaker><by_whom>Vice President of the United States</by_whom><tie_breaker_vote>Yea</tie_breaker_vote></tie_breaker>";
        var members = SenateVoteFixtures.DefaultMembers.Replace("<vote_cast>Not Voting</vote_cast>", "<vote_cast>Nay</vote_cast>");
        var count = "<count><yeas>1</yeas><nays>1</nays><present/><absent/></count>";

        var detail = Parse(SenateVoteFixtures.Vote(tieBreaker: tieBreaker, members: members, count: count));

        Assert.Equal(new SenateVoteCounts(1, 1, null, null), detail.Counts);
        Assert.Equal(2, detail.Members.Count);
    }

    [Theory]
    [InlineData("Guilty")]
    [InlineData("Not Guilty")]
    [InlineData("Present, Giving Live Pair")]
    public void ACastOutsideTheFourCountedKindsFailsTheFileBecauseNoCountCoversIt(string cast)
    {
        var members = SenateVoteFixtures.DefaultMembers.Replace("<vote_cast>Not Voting</vote_cast>", $"<vote_cast>{cast}</vote_cast>");

        Assert.Throws<InvalidDataException>(() => Parse(SenateVoteFixtures.Vote(members: members)));
    }

    [Fact]
    public void AVoteWithoutADocumentIsAVoteWithNoBillBecauseEveryRollCallIsCollected()
    {
        var detail = Parse(SenateVoteFixtures.Vote(document: "<document><document_type/><document_number/></document>"));

        Assert.Null(detail.DocumentName);
        Assert.Null(detail.Vote.BillType);
    }

    [Fact]
    public void MembersKeepTheirLisIdAndTheirCastAsWrittenBecauseTheRosterMatchesOnTheLisId()
    {
        var members = Parse(SenateVoteFixtures.Vote()).Members;

        Assert.Equal(
            [new SenateVoteMember("S428", "Angela", "Alsobrooks", "D", "MD", "Yea"),
             new SenateVoteMember("S429", "Jim", "Banks", "R", "IN", "Not Voting")],
            members);
    }

    [Fact]
    public void CountsKeepEmptyFieldsAsAbsent()
    {
        Assert.Equal(new SenateVoteCounts(1, 0, null, 1), Parse(SenateVoteFixtures.Vote()).Counts);
    }

    [Fact]
    public void QuestionAndTitleHaveTheirWhitespaceCollapsed()
    {
        var detail = Parse(SenateVoteFixtures.Vote(question: "On the Cloture Motion\n         "));

        Assert.Equal("On the Cloture Motion", detail.Question);
    }

    [Fact]
    public void AFileForAnotherRollThanRequestedIsInvalidBecauseTheJobUrlNamesTheRoll()
    {
        Assert.Throws<InvalidDataException>(() => Parse(SenateVoteFixtures.Vote(voteNumber: "2")));
    }

    [Theory]
    [InlineData("modifyDate", "yesterday")]
    [InlineData("voteDate", "January 9, 2025")]
    [InlineData("members", "")]
    [InlineData("members", "<member><last_name>Banks</last_name></member>")]
    [InlineData("count", "<count><yeas>x</yeas></count>")]
    [InlineData("count", "")]
    public void AShapeTheParserDoesNotKnowIsInvalidInsteadOfGuessed(string field, string value)
    {
        var xml = field switch
        {
            "modifyDate" => SenateVoteFixtures.Vote(modifyDate: value),
            "voteDate" => SenateVoteFixtures.Vote(voteDate: value),
            "members" => SenateVoteFixtures.Vote(members: value),
            _ => SenateVoteFixtures.Vote(count: value)
        };

        Assert.Throws<InvalidDataException>(() => Parse(xml));
    }

    [Theory]
    [InlineData("<count><yeas>2</yeas><nays>0</nays><present/><absent>1</absent></count>")]
    [InlineData("<count><yeas>1</yeas><nays>0</nays><present/><absent>0</absent></count>")]
    [InlineData("<count><yeas>1</yeas><nays>0</nays><present>1</present><absent>1</absent></count>")]
    public void CountsThatDifferFromTheMemberListAreInvalidBecauseOneOfThemIsWrong(string count)
    {
        Assert.Throws<InvalidDataException>(() => Parse(SenateVoteFixtures.Vote(count: count)));
    }

    [Fact]
    public void ADuplicateMemberIsInvalidBecauseOneSenatorCastsOneVote()
    {
        var members = SenateVoteFixtures.DefaultMembers + SenateVoteFixtures.DefaultMembers;

        Assert.Throws<InvalidDataException>(() => Parse(SenateVoteFixtures.Vote(members: members)));
    }

    private static SenateVoteDetail Parse(byte[] xml) => SenateVoteParser.Parse(xml, SenateVoteFixtures.Roll1);
}
