using System.Text.Json;
using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedVoteTests
{
    private const string DetailId = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string MembersId = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string DetailText = "Started: 2025-04-10T11:08:00-04:00\nQuestion: On Passage\nResult: Passed\nLegislation: HR 7\n";
    private const string MembersText = "Member: A000055, Robert Aderholt, R, AL, Yea\nMember: B000001, Ann Roe, D, MA, Nay\n";

    [Fact]
    public void ValidVotePasses()
    {
        Vote().Validate();
    }

    [Fact]
    public void TheEntryNamesNoOfficialUntilAnOfficialPageOrSearchServiceIndexesVotesAndDatesTheVoteByItsDay()
    {
        var entry = Vote().ToEntry();

        Assert.Equal(PublishedRecordKind.Vote, entry.Kind);
        Assert.Empty(entry.Officials);
        Assert.Equal(["housing"], entry.IssueIds);
        Assert.Empty(entry.PolicyAreaIds);
        Assert.Equal(new DateOnly(2025, 4, 10), entry.EventDate);
        Assert.Equal("api.congress.gov", entry.SourceHost);
        entry.Validate();
    }

    [Fact]
    public void TheJsonShapeIsPinnedSoReadersOfOlderReleasesKeepWorking()
    {
        var json = JsonSerializer.Serialize(Vote(), PublicationProtocol.JsonOptions);

        Assert.Equal(["recordId", "revisionNumber", "headline", "summary", "congress", "session", "rollCallNumber", "kind", "kindCitation",
            "votedOn", "votedOnCitation", "facts", "members", "billType", "billNumber", "trackedIssueIds", "sources", "approvedAtUtc",
            "firstPublishedAtUtc"], JsonDocument.Parse(json).RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Contains("\"kind\":\"passage\"", json, StringComparison.Ordinal);
        Assert.Contains("\"votedOn\":\"2025-04-10\"", json, StringComparison.Ordinal);
        JsonSerializer.Deserialize<PublishedVote>(json, PublicationProtocol.JsonOptions)!.Validate();
    }

    // The quote is exact text of the other source, so only a check against the named source finds the wrong name.
    [Fact]
    public void RejectsACitationWhoseQuoteMatchesAnotherSourceButNotTheOneItNames()
    {
        var vote = Vote();
        var misnamed = vote.Members[1] with { Citation = vote.Members[1].Citation with { ExtractionId = DetailId } };

        Assert.Throws<InvalidDataException>((vote with { Members = [vote.Members[0], misnamed] }).Validate);
    }

    [Fact]
    public void RejectsACitationThatDoesNotQuoteItsSourceExactly()
    {
        var vote = Vote();
        var altered = vote.VotedOnCitation with { Quote = "Started: 2025-04-11T15:08:00Z" };

        Assert.Throws<InvalidDataException>((vote with { VotedOnCitation = altered }).Validate);
    }

    [Fact]
    public void RejectsACitationThatNamesNoEmbeddedSource()
    {
        var vote = Vote();

        var error = Assert.Throws<InvalidDataException>((vote with { VotedOnCitation = vote.VotedOnCitation with { ExtractionId = new string('9', 64) } }).Validate);

        Assert.Equal("Published citation must cite one of the vote's sources.", error.Message);
    }

    [Fact]
    public void TheVoteDayIsTheDateOfTheCitedStartLineSoALateVoteKeepsItsHouseDayAcrossUtcMidnight()
    {
        const string evening = "Started: 2025-07-02T21:15:00-04:00\n";
        var vote = Vote();
        var cited = vote with
        {
            VotedOn = new DateOnly(2025, 7, 2),
            VotedOnCitation = Cite(DetailId, evening, "Started: 2025-07-02T21:15:00-04:00"),
            Facts = [],
            KindCitation = null,
            Kind = PublishedVoteKind.Other,
            Sources = [vote.Sources[0] with { Text = evening }, vote.Sources[1]]
        };

        cited.Validate();
        Assert.Throws<InvalidDataException>((cited with { VotedOn = new DateOnly(2025, 7, 3) }).Validate);
    }

    [Fact]
    public void ANullCitationQuoteIsInvalidDataAndNotAnUnhandledError()
    {
        var vote = Vote();
        var broken = vote.Members[0] with { Citation = vote.Members[0].Citation with { Quote = null! } };

        Assert.Throws<InvalidDataException>((vote with { Members = [broken, vote.Members[1]] }).Validate);
    }

    [Fact]
    public void RejectsASourceThatNoCitationNamesBecauseTheFileEmbedsOnlyCitedEvidence()
    {
        var vote = Vote();
        var extra = vote.Sources[1] with { ExtractionId = new string('3', 64) };

        Assert.Throws<InvalidDataException>((vote with { Sources = [.. vote.Sources, extra] }).Validate);
    }

    [Fact]
    public void RejectsTheSameSourceTwiceBecauseEachExtractionIsEmbeddedOnce()
    {
        var vote = Vote();

        Assert.Throws<InvalidDataException>((vote with { Sources = [.. vote.Sources, vote.Sources[0]] }).Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PublishedVote.MaximumSources + 1)]
    public void RequiresOneToThreeSourcesSoTheRecordFileStaysBounded(int count)
    {
        var vote = Vote();
        var sources = Enumerable.Range(0, count).Select(index => vote.Sources[0] with { ExtractionId = index.ToString("x64") });

        Assert.Throws<InvalidDataException>((vote with { Sources = [.. sources] }).Validate);
    }

    [Fact]
    public void AKindOtherThanOtherNeedsItsCitationAndOtherNeedsNone()
    {
        var vote = Vote();

        Assert.Throws<InvalidDataException>((vote with { KindCitation = null }).Validate);
        (vote with { Kind = PublishedVoteKind.Other, KindCitation = null }).Validate();
    }

    [Fact]
    public void AFactMustEqualItsCitedQuoteSoNoFactStatesMoreThanTheSourceSays()
    {
        var vote = Vote();

        Assert.Throws<InvalidDataException>((vote with { Facts = [vote.Facts[0] with { Text = "Result: Failed" }] }).Validate);
    }

    [Fact]
    public void ACastMustBeTheWordsItsCitationShowsSoNoVoteIsPublishedThatTheLineDoesNotState()
    {
        var vote = Vote();

        Assert.Throws<InvalidDataException>((vote with { Members = [vote.Members[0] with { Cast = "Nay" }, vote.Members[1]] }).Validate);
    }

    [Fact]
    public void ACastIsTheClerksOwnWordsSoACandidateNameInASpeakerElectionPasses()
    {
        var text = "Member: A000055, Robert Aderholt, R, AL, Jeffries\n";
        var member = Vote().Members[0] with { Cast = "Jeffries", Citation = Cite(MembersId, text, "Member: A000055, Robert Aderholt, R, AL, Jeffries") };
        var vote = Vote();

        (vote with { Members = [member], Sources = [vote.Sources[0], vote.Sources[1] with { Text = text }] }).Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PublishedVote.MaximumMembers + 1)]
    public void RequiresOneToSixHundredMembers(int count)
    {
        var vote = Vote();

        Assert.Throws<InvalidDataException>((vote with { Members = [.. Enumerable.Repeat(vote.Members[0], count)] }).Validate);
    }

    [Fact]
    public void ARecordListsEachOfficialOnceSoTheFilterNeverRepeatsAnId()
    {
        var vote = Vote();

        Assert.Throws<InvalidDataException>((vote with { Members = [vote.Members[0], vote.Members[0]] }).Validate);
    }

    [Fact]
    public void ABillTypeNeedsANumberAndANumberNeedsAType()
    {
        (Vote() with { BillType = null, BillNumber = null }).Validate();

        Assert.Throws<InvalidDataException>((Vote() with { BillNumber = null }).Validate);
        Assert.Throws<InvalidDataException>((Vote() with { BillType = null }).Validate);
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(119, 3, 1)]
    [InlineData(119, 1, 0)]
    public void RequiresARollCallIdentity(int congress, int session, int roll)
    {
        Assert.Throws<InvalidDataException>((Vote() with { Congress = congress, Session = session, RollCallNumber = roll }).Validate);
    }

    [Fact]
    public void RequiresAVoteIdSoAVoteCannotUseADocumentChangeId()
    {
        Assert.Throws<InvalidDataException>((Vote() with { RecordId = "0123456789abcdef0123456789abcdef" }).Validate);
    }

    [Fact]
    public void ContractLimitsEqualTheCoreLimits()
    {
        Assert.Equal(VoteDraftRevision.MaximumMembers, PublishedVote.MaximumMembers);
        Assert.Equal(VoteDraftRevision.MaximumFacts, PublishedVote.MaximumFacts);
    }

    [Fact]
    public void ContractKindsMirrorTheCoreKinds()
    {
        Assert.Equal(Enum.GetNames<VoteKind>(), Enum.GetNames<PublishedVoteKind>());
    }

    private static PublishedVote Vote() => new()
    {
        RecordId = new string('5', 64),
        RevisionNumber = 1,
        Headline = "Headline",
        Summary = "Summary",
        Congress = 119,
        Session = 1,
        RollCallNumber = 100,
        Kind = PublishedVoteKind.Passage,
        KindCitation = Cite(DetailId, DetailText, "Question: On Passage"),
        VotedOn = new DateOnly(2025, 4, 10),
        VotedOnCitation = Cite(DetailId, DetailText, "Started: 2025-04-10T11:08:00-04:00"),
        Facts = [Fact("Started: 2025-04-10T11:08:00-04:00"), Fact("Question: On Passage"), Fact("Result: Passed"), Fact("Legislation: HR 7")],
        Members =
        [
            new() { Official = new() { Id = "aderholt", Name = "Robert Aderholt" }, Cast = "Yea",
                Citation = Cite(MembersId, MembersText, "Member: A000055, Robert Aderholt, R, AL, Yea") },
            new() { Official = new() { Id = "roe", Name = "Ann Roe" }, Cast = "Nay",
                Citation = Cite(MembersId, MembersText, "Member: B000001, Ann Roe, D, MA, Nay") }
        ],
        BillType = "hr",
        BillNumber = 7,
        TrackedIssueIds = ["housing"],
        Sources =
        [
            Source(DetailId, "https://api.congress.gov/v3/house-vote/119/1/100", DetailText),
            Source(MembersId, "https://api.congress.gov/v3/house-vote/119/1/100/members", MembersText)
        ],
        ApprovedAtUtc = DateTimeOffset.UnixEpoch,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
    };

    private static PublishedVoteFact Fact(string text) => new() { Text = text, Citation = Cite(DetailId, DetailText, text) };

    private static PublishedCitation Cite(string extractionId, string text, string quote) => new()
    {
        ExtractionId = extractionId,
        Start = text.IndexOf(quote, StringComparison.Ordinal),
        Length = quote.Length,
        Quote = quote
    };

    private static PublishedDocumentVersion Source(string extractionId, string url, string text) => new()
    {
        ExtractionId = extractionId,
        Url = url,
        ObservedAtUtc = DateTimeOffset.UnixEpoch,
        Text = text
    };
}
