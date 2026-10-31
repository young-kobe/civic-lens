using CivicLens.Core.Actions;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Publication.Contracts;

public sealed class PublishedActionTests
{
    private const string DetailId = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string CosponsorsId = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string DetailText = "Sponsor: M000001\nPolicy area: Housing\n";
    private const string CosponsorsText = "Cosponsor: C000001, 2026-01-05\n";

    [Fact]
    public void PublishedActionJsonMatchesPinnedSchemaVersion()
    {
        var json = JsonSerializer.Serialize(Action(), PublicationProtocol.JsonOptions);

        Assert.Equal(1, PublicationProtocol.SchemaVersion);
        Assert.True(json == """{"recordId":"5555555555555555555555555555555555555555555555555555555555555555","revisionNumber":1,"headline":"Headline","summary":"Summary","textOrigin":"model","congress":119,"billType":"hr","billNumber":7,"billTitle":"A housing bill","policyArea":"Housing","policyAreaId":"housing-area","policyAreaCitation":{"extractionId":"1111111111111111111111111111111111111111111111111111111111111111","start":17,"length":20,"quote":"Policy area: Housing"},"trackedIssueIds":["housing"],"summaryCitation":null,"sponsorships":[{"official":{"id":"mayor","name":"Mayor Example"},"role":"sponsor","isOriginal":false,"sponsoredOn":"2026-01-03","withdrawnOn":null,"citations":[{"extractionId":"1111111111111111111111111111111111111111111111111111111111111111","start":0,"length":16,"quote":"Sponsor: M000001"}]},{"official":{"id":"chair","name":"Chair Example"},"role":"cosponsor","isOriginal":true,"sponsoredOn":"2026-01-05","withdrawnOn":"2026-01-20","citations":[{"extractionId":"2222222222222222222222222222222222222222222222222222222222222222","start":0,"length":30,"quote":"Cosponsor: C000001, 2026-01-05"}]}],"sources":[{"extractionId":"1111111111111111111111111111111111111111111111111111111111111111","url":"https://api.congress.gov/v3/bill/119/hr/7","observedAtUtc":"1970-01-01T00:00:00+00:00","text":"Sponsor: M000001\nPolicy area: Housing\n"},{"extractionId":"2222222222222222222222222222222222222222222222222222222222222222","url":"https://api.congress.gov/v3/bill/119/hr/7/cosponsors","observedAtUtc":"1970-01-01T00:00:00+00:00","text":"Cosponsor: C000001, 2026-01-05\n"}],"approvedAtUtc":"1970-01-01T00:00:00+00:00","firstPublishedAtUtc":"1970-01-01T00:00:00+00:00","corrections":[]}""",
            "The published action JSON changed. Bump PublicationProtocol.SchemaVersion, update the version table " +
            "in docs/architecture.md, then update this test. Actual: " + json);
    }

    [Fact]
    public void CorrectionsRoundTripInTheRecordFileSoAReaderShowsWhatThePublishWrote()
    {
        var corrections = new PublishedCorrection[]
        {
            new() { ReplacedRevisionNumber = 2, PublishedAtUtc = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.Zero), Note = "Fixed the sponsor date." },
            new() { ReplacedRevisionNumber = 1, PublishedAtUtc = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero) }
        };
        var json = JsonSerializer.Serialize(Action() with { RevisionNumber = 3, Corrections = corrections }, PublicationProtocol.JsonOptions);

        var read = JsonSerializer.Deserialize<PublishedAction>(json, PublicationProtocol.JsonOptions)!;

        read.Validate();
        Assert.Equal(corrections, read.Corrections);
    }

    [Fact]
    public void ARecordThatWasNeverReplacedListsNoCorrections()
    {
        var replaced = new PublishedCorrection { ReplacedRevisionNumber = 1, PublishedAtUtc = DateTimeOffset.UnixEpoch };

        Assert.Throws<InvalidDataException>((Action() with { Corrections = [replaced] }).Validate);
    }

    [Fact]
    public void ValidActionPasses()
    {
        Action().Validate();
    }

    [Fact]
    public void TheEntryNamesOnlyTheSponsorAndDatesTheLatestSponsorshipEvent()
    {
        var entry = Action().ToEntry();

        Assert.Equal(["mayor"], entry.Officials.Select(official => official.Id));
        Assert.Equal(new DateOnly(2026, 1, 20), entry.EventDate);
        Assert.Equal(["housing-area"], entry.PolicyAreaIds);
        Assert.Equal("api.congress.gov", entry.SourceHost);
        Assert.Equal("A housing bill", entry.BillTitle);
    }

    [Fact]
    public void TheEntryListsTheCosponsorIdsApartFromTheSponsorsSoACosponsorNeverCountsAsASponsoredBill()
    {
        var entry = WithChairCosponsoring().ToEntry();

        Assert.Equal(["chair"], entry.CosponsorIds!);
        Assert.DoesNotContain(entry.Officials, official => official.Id == "chair");
        entry.Validate();
    }

    [Fact]
    public void AnActionWithNoCosponsorGetsAnEmptyListBecauseNoCosponsorIsADataPointAndNullMeansNoData()
    {
        var action = Action() with { Sponsorships = [.. Action().Sponsorships.Where(item => item.Role == PublishedSponsorshipRole.Sponsor)] };

        Assert.Empty(action.ToEntry().CosponsorIds!);
    }

    [Fact]
    public void AWithdrawnCosponsorshipIsNotListedBecauseTheOfficialNoLongerBacksTheBill()
    {
        var entry = Action().ToEntry();

        Assert.NotNull(Action().Sponsorships.Single(item => item.Role == PublishedSponsorshipRole.Cosponsor).WithdrawnOn);
        Assert.Empty(entry.CosponsorIds!);
    }

    [Fact]
    public void ASponsorWhoAlsoCosponsorsIsListedAsASponsorOnlySoTheBillNeverCountsTwiceForOneOfficial()
    {
        var action = WithChairCosponsoring() with
        {
            Sponsorships = [.. WithChairCosponsoring().Sponsorships, Cosponsorship("mayor", "Mayor Example")]
        };

        var entry = action.ToEntry();

        Assert.Equal(["chair"], entry.CosponsorIds!);
        Assert.Equal(["mayor"], entry.Officials.Select(official => official.Id));
    }

    [Fact]
    public void TwoCosponsorRowsOfOneOfficialGiveOneIdSoTheEntryStaysValidAndThePublishGoesOn()
    {
        var action = WithChairCosponsoring() with
        {
            Sponsorships = [.. WithChairCosponsoring().Sponsorships, Cosponsorship("chair", "Chair Example")]
        };

        var entry = action.ToEntry();

        Assert.Equal(["chair"], entry.CosponsorIds!);
        entry.Validate();
    }

    [Fact]
    public void TheBillTitleIsBoundedInTheRecordAndInItsEntryAtTheLimitOfTheReviewSoTheTwoNeverDisagree()
    {
        Assert.Equal(ActionDraftRevision.MaximumTitleLength, PublishedAction.MaximumBillTitleLength);
        var atLimit = Action() with { BillTitle = new string('t', PublishedAction.MaximumBillTitleLength) };
        var tooLong = Action() with { BillTitle = new string('t', PublishedAction.MaximumBillTitleLength + 1) };

        atLimit.Validate();
        atLimit.ToEntry().Validate();
        Assert.Throws<InvalidDataException>(tooLong.Validate);
        Assert.Throws<InvalidDataException>(tooLong.ToEntry().Validate);
        Assert.Throws<InvalidDataException>((Action().ToEntry() with { BillTitle = " " }).Validate);
    }

    [Fact]
    public void AnActionEntryRequiresABillTitleAndEveryOtherKindRefusesOneBecauseOnlyAnActionHasAnOfficialTitle()
    {
        var entry = Action().ToEntry();

        Assert.Throws<InvalidDataException>((entry with { BillTitle = null }).Validate);
        Assert.Throws<InvalidDataException>((entry with { Kind = PublishedRecordKind.Statement, RecordId = new string('1', 64) }).Validate);
        (entry with { Kind = PublishedRecordKind.Statement, RecordId = new string('1', 64), BillTitle = null, CosponsorIds = null }).Validate();
    }

    // The quote is exact text of the other source, so only a check against the named source finds the wrong name.
    [Fact]
    public void RejectsACitationWhoseQuoteMatchesAnotherSourceButNotTheOneItNames()
    {
        var action = Action();
        var cosponsor = action.Sponsorships[1];
        var misnamed = cosponsor with { Citations = [cosponsor.Citations[0] with { ExtractionId = DetailId }] };

        Assert.Throws<InvalidDataException>((action with { Sponsorships = [action.Sponsorships[0], misnamed] }).Validate);
    }

    [Fact]
    public void RejectsACitationThatNamesNoEmbeddedSource()
    {
        var action = Action();
        var citation = action.PolicyAreaCitation! with { ExtractionId = new string('9', 64) };

        var error = Assert.Throws<InvalidDataException>((action with { PolicyAreaCitation = citation }).Validate);

        Assert.Equal("Published citation must cite one of the action's sources.", error.Message);
    }

    [Fact]
    public void RejectsASourceThatNoCitationNamesBecauseTheFileEmbedsOnlyCitedEvidence()
    {
        var action = Action();
        var extra = action.Sources[1] with { ExtractionId = new string('3', 64) };

        Assert.Throws<InvalidDataException>((action with { Sources = [.. action.Sources, extra] }).Validate);
    }

    [Fact]
    public void RejectsTheSameSourceTwiceBecauseEachExtractionIsEmbeddedOnce()
    {
        var action = Action();

        Assert.Throws<InvalidDataException>((action with { Sources = [.. action.Sources, action.Sources[0]] }).Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PublishedAction.MaximumSources + 1)]
    public void RequiresOneToEightSourcesSoTheRecordFileStaysBounded(int count)
    {
        var action = Action();
        var sources = Enumerable.Range(0, count).Select(index => action.Sources[0] with { ExtractionId = index.ToString("x64") });

        Assert.Throws<InvalidDataException>((action with { Sources = [.. sources] }).Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PublishedAction.MaximumSponsorships + 1)]
    public void RequiresOneToSixHundredSponsorships(int count)
    {
        var action = Action();

        Assert.Throws<InvalidDataException>((action with { Sponsorships = [.. Enumerable.Repeat(action.Sponsorships[0], count)] }).Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(PublishedAction.MaximumCitationsPerSponsorship + 1)]
    public void RequiresOneOrTwoCitationsForEachSponsorship(int count)
    {
        var action = Action();
        var sponsor = action.Sponsorships[0];
        var cited = sponsor with { Citations = [.. Enumerable.Repeat(sponsor.Citations[0], count)] };

        Assert.Throws<InvalidDataException>((action with { Sponsorships = [cited, action.Sponsorships[1]] }).Validate);
    }

    [Fact]
    public void APolicyAreaNeedsItsEvidence()
    {
        Assert.Throws<InvalidDataException>((Action() with { PolicyAreaCitation = null }).Validate);
    }

    [Fact]
    public void ASponsorshipCannotBeWithdrawnBeforeItBegan()
    {
        var action = Action();
        var sponsor = action.Sponsorships[0];
        var withdrawn = sponsor with { WithdrawnOn = sponsor.SponsoredOn.AddDays(-1) };

        Assert.Throws<InvalidDataException>((action with { Sponsorships = [withdrawn, action.Sponsorships[1]] }).Validate);
    }

    [Fact]
    public void APolicyAreaIdNeedsAPolicyAreaSoTheFilterNeverNamesAnAreaTheRecordLacks()
    {
        var action = Action() with { PolicyArea = null, PolicyAreaCitation = null, PolicyAreaId = "housing-area" };

        Assert.Throws<InvalidDataException>(action.Validate);
    }

    [Fact]
    public void ContractLimitsEqualTheCoreLimits()
    {
        Assert.Equal(ActionDraftRevision.MaximumSponsorships, PublishedAction.MaximumSponsorships);
        Assert.Equal(ActionDraftRevision.MaximumCitationsPerSponsorship, PublishedAction.MaximumCitationsPerSponsorship);
    }

    [Fact]
    public void RequiresAnActionIdSoAnActionCannotUseADocumentChangeId()
    {
        Assert.Throws<InvalidDataException>((Action() with { RecordId = "0123456789abcdef0123456789abcdef" }).Validate);
    }

    private static PublishedAction WithChairCosponsoring() => Action() with
    {
        Sponsorships = [.. Action().Sponsorships.Select(item => item.Role == PublishedSponsorshipRole.Cosponsor ? item with { WithdrawnOn = null } : item)]
    };

    private static PublishedSponsorship Cosponsorship(string id, string name) => new()
    {
        Official = new() { Id = id, Name = name },
        Role = PublishedSponsorshipRole.Cosponsor,
        IsOriginal = false,
        SponsoredOn = new DateOnly(2026, 1, 6),
        Citations = [Cite(CosponsorsId, CosponsorsText, "Cosponsor: C000001, 2026-01-05")]
    };

    private static PublishedAction Action() => new()
    {
        Corrections = [],
        RecordId = new string('5', 64),
        RevisionNumber = 1,
        Headline = "Headline",
        Summary = "Summary",
        TextOrigin = PublishedTextOrigin.Model,
        Congress = 119,
        BillType = "hr",
        BillNumber = 7,
        BillTitle = "A housing bill",
        PolicyArea = "Housing",
        PolicyAreaId = "housing-area",
        PolicyAreaCitation = Cite(DetailId, DetailText, "Policy area: Housing"),
        TrackedIssueIds = ["housing"],
        Sponsorships =
        [
            new()
            {
                Official = new() { Id = "mayor", Name = "Mayor Example" },
                Role = PublishedSponsorshipRole.Sponsor,
                IsOriginal = false,
                SponsoredOn = new DateOnly(2026, 1, 3),
                Citations = [Cite(DetailId, DetailText, "Sponsor: M000001")]
            },
            new()
            {
                Official = new() { Id = "chair", Name = "Chair Example" },
                Role = PublishedSponsorshipRole.Cosponsor,
                IsOriginal = true,
                SponsoredOn = new DateOnly(2026, 1, 5),
                WithdrawnOn = new DateOnly(2026, 1, 20),
                Citations = [Cite(CosponsorsId, CosponsorsText, "Cosponsor: C000001, 2026-01-05")]
            }
        ],
        Sources =
        [
            Source(DetailId, "https://api.congress.gov/v3/bill/119/hr/7", DetailText),
            Source(CosponsorsId, "https://api.congress.gov/v3/bill/119/hr/7/cosponsors", CosponsorsText)
        ],
        ApprovedAtUtc = DateTimeOffset.UnixEpoch,
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch
    };

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
