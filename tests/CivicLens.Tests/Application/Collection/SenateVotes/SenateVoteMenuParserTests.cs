using System.Text;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.SenateVotes;

public sealed class SenateVoteMenuParserTests
{
    [Fact]
    public void MenuListsEachVoteWithItsRollAndItsDayInTheYearOfTheSessionBecauseTheMenuDateHasNoYear()
    {
        var menu = SenateVoteMenuParser.Parse(SenateVoteFixtures.Menu(), SenateVoteFixtures.Session);

        Assert.Equal(
            [new SenateVoteMenuItem(new SenateVoteRoll(SenateVoteFixtures.Session, 659), new DateOnly(2025, 12, 18)),
             new SenateVoteMenuItem(SenateVoteFixtures.Roll1, new DateOnly(2025, 1, 9))],
            menu.Votes);
    }

    [Fact]
    public void MenuOfAnotherSessionThanRequestedIsInvalidBecauseItsVotesWouldBelongToTheWrongRolls()
    {
        Assert.Throws<InvalidDataException>(() =>
            SenateVoteMenuParser.Parse(SenateVoteFixtures.Menu(), new VoteSession(119, 2)));
    }

    [Fact]
    public void MenuWhoseYearDisagreesWithItsSessionIsInvalidBecauseTheDayWouldBeWrong()
    {
        Assert.Throws<InvalidDataException>(() =>
            SenateVoteMenuParser.Parse(SenateVoteFixtures.Menu(congressYear: "2026"), SenateVoteFixtures.Session));
    }

    [Theory]
    [InlineData("<vote><vote_number>00000</vote_number><vote_date>09-Jan</vote_date></vote>")]
    [InlineData("<vote><vote_number>x</vote_number><vote_date>09-Jan</vote_date></vote>")]
    [InlineData("<vote><vote_number>00001</vote_number><vote_date>31-Feb</vote_date></vote>")]
    [InlineData("<vote><vote_number>00001</vote_number></vote>")]
    [InlineData("<vote><vote_number>00001</vote_number><vote_date>09-Jan</vote_date></vote><vote><vote_number>1</vote_number><vote_date>10-Jan</vote_date></vote>")]
    public void AMenuEntryWithoutAValidNumberAndDayOrListedTwiceIsInvalidBecauseNoVoteMayBeGuessed(string votes)
    {
        Assert.Throws<InvalidDataException>(() =>
            SenateVoteMenuParser.Parse(SenateVoteFixtures.Menu(votes), SenateVoteFixtures.Session));
    }

    [Fact]
    public void ADocumentTypeDeclarationIsRefusedBecauseTheFileComesFromTheNetwork()
    {
        var xml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!DOCTYPE vote_summary [<!ENTITY a \"b\">]><vote_summary/>");

        Assert.Throws<InvalidDataException>(() => SenateVoteMenuParser.Parse(xml, SenateVoteFixtures.Session));
    }

    [Fact]
    public void ARootOfAnotherNameIsInvalid()
    {
        var xml = Encoding.UTF8.GetBytes("<roll_call_vote/>");

        Assert.Throws<InvalidDataException>(() => SenateVoteMenuParser.Parse(xml, SenateVoteFixtures.Session));
    }
}
