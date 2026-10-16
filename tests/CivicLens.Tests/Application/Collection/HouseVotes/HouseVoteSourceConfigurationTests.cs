using CivicLens.Application.Collection;
using CivicLens.Application.Collection.HouseVotes;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.HouseVotes;

public sealed class HouseVoteSourceConfigurationTests
{
    private const string Source = HouseVoteFixtures.SourceUrl;
    private static readonly DateOnly Today = new(2026, 10, 16);

    [Fact]
    public void VoteSourceCoversEveryPersonWithABioguideIdFromTheBackfillDate()
    {
        var configuration = HouseVoteFixtures.Configuration();
        var source = configuration.Sources.Single();

        Assert.Equal(["with-id-a"], configuration.CoveredPersonIdsOf(source, HouseVoteFixtures.Backfill));
        Assert.Empty(configuration.CoveredPersonIdsOf(source, HouseVoteFixtures.Backfill.AddDays(-1)));
        Assert.Empty(configuration.ListedPersonIdsOf(source));
    }

    [Fact]
    public void ParserOptionsSurviveARevisionRoundTrip()
    {
        var source = CollectionConfigurationRevision.Create(HouseVoteFixtures.Configuration()).ReadConfiguration().Sources.Single();

        Assert.Equal(ApiParser.CongressHouseVoteList, source.ApiParser);
        Assert.Equal(119, source.MinimumCongress);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(null, true)]
    [InlineData(119, false)]
    public void MinimumCongressAndBackfillDateAreRequiredWithTheParser(int? minimumCongress, bool hasBackfill)
    {
        var configuration = HouseVoteFixtures.Configuration();
        var source = configuration.Sources.Single() with
        {
            MinimumCongress = minimumCongress,
            BackfillFrom = hasBackfill ? HouseVoteFixtures.Backfill : null
        };

        Assert.Throws<ArgumentException>((configuration with { Sources = [source] }).Validate);
    }

    [Fact]
    public void AVoteSourceNeedsACredentialAndCannotListPeople()
    {
        var configuration = HouseVoteFixtures.Configuration();
        var source = configuration.Sources.Single();

        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { Credential = null }] }).Validate);
        Assert.Throws<ArgumentException>((configuration with
        {
            Sources = [source with { Coverage = [new SourceCoverageConfiguration { PersonId = "with-id-a" }] }]
        }).Validate);
    }

    [Theory]
    [InlineData(250, 1506, 753_000_000, 15_060)]
    [InlineData(251, 1505, 753_000_000, 15_060)]
    [InlineData(251, 1506, 752_999_999, 15_060)]
    [InlineData(251, 1506, 753_000_000, 15_059)]
    public void AdmissionPolicyMustHoldAFullPageOfDetailJobsAndTheNextPageJob(int maxJobs, int requests, long bytes, int seconds)
    {
        var configuration = HouseVoteFixtures.Configuration();
        var source = configuration.Sources.Single();
        var policy = source.AdmissionPolicy! with
        {
            MaxJobs = maxJobs,
            MaxTotalRequests = requests,
            MaxTotalBytes = bytes,
            MaxTotalTimeoutSeconds = seconds
        };

        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { AdmissionPolicy = policy }] }).Validate);
        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { AdmissionPolicy = null }] }).Validate);
        configuration.Validate();
    }

    [Theory]
    [InlineData(ApiResource.HouseVoteList, Source + "/119/1?format=json&limit=250")]
    [InlineData(ApiResource.HouseVoteList, Source + "/119/2?offset=250&limit=250&format=json")]
    [InlineData(ApiResource.HouseVoteDetail, Source + "/119/1/100?format=json")]
    [InlineData(ApiResource.HouseVoteMembers, Source + "/119/1/100/members?format=json")]
    public void JobsAcceptTheExactUrlFormOfTheirResourceKind(ApiResource resource, string url)
    {
        var job = Configured().CreateApiJobDefinition(resource, url, Today);

        Assert.Equal(resource, job.ApiResource);
        Assert.Equal(CollectionCredentials.CongressGov, job.Credential);
    }

    [Theory]
    [InlineData(ApiResource.HouseVoteDetail, Source + "/119/1/100/members?format=json")]
    [InlineData(ApiResource.HouseVoteMembers, Source + "/119/1/100?format=json")]
    [InlineData(ApiResource.HouseVoteList, Source + "/119/1/100?format=json")]
    [InlineData(ApiResource.HouseVoteList, Source + "/119/1?format=json&limit=250&api_key=secret")]
    [InlineData(ApiResource.HouseVoteDetail, Source + "/119/1/100?format=json&api_key=secret")]
    [InlineData(ApiResource.HouseVoteMembers, Source + "/119/1/100/members?format=json&limit=3")]
    [InlineData(ApiResource.HouseVoteDetail, "https://api.congress.gov/v3/bill/119/hr/1?format=json")]
    [InlineData(ApiResource.Bill, Source + "/119/1/100?format=json")]
    [InlineData(ApiResource.BillList, Source + "/119/1?format=json&limit=250")]
    public void AJobUrlMustHaveTheExactFormOfItsResourceSoTheWorkerNeverReadsItAsAnotherKind(ApiResource resource, string url)
    {
        Assert.Throws<ArgumentException>(() => Configured().CreateApiJobDefinition(resource, url, Today));
    }

    [Fact]
    public void AVoteResourceKindIsRefusedByABillSource()
    {
        var bills = new ConfiguredCollectionSource(CongressBillSourceConfigurationTests.Configuration(), "congress-bills", HouseVoteFixtures.Backfill);

        Assert.Throws<ArgumentException>(() => bills.CreateApiJobDefinition(ApiResource.HouseVoteDetail,
            "https://api.congress.gov/v3/bill/119/1/100?format=json", Today));
    }

    [Fact]
    public void EveryListPageIsARootCheckAndDetailAndMembersJobsAreNot()
    {
        var configured = Configured();

        Assert.True(configured.CreateApiJobDefinition(ApiResource.HouseVoteList, Source + "/119/1?format=json&limit=250", Today).IsRoot());
        Assert.False(configured.CreateApiJobDefinition(ApiResource.HouseVoteDetail, Source + "/119/1/100?format=json", Today).IsRoot());
        Assert.False(configured.CreateApiJobDefinition(ApiResource.HouseVoteMembers, Source + "/119/1/100/members?format=json", Today).IsRoot());
    }

    [Fact]
    public void AListPageAdmitsOneDetailJobForEachRollCallAndItsNextPage()
    {
        var next = Source + "/119/1?offset=250&limit=250&format=json";
        var page = new HouseVoteListPage(HouseVoteFixtures.FirstSession,
            [Item(240), Item(241), Item(240)], next);

        var batch = Configured().CreateHouseVoteListBatch("list-job", page, Today);

        Assert.Equal([Source + "/119/1/240?format=json", Source + "/119/1/241?format=json", next], batch.Jobs.Select(job => job.Definition.Url));
        Assert.Equal([ApiResource.HouseVoteDetail, ApiResource.HouseVoteDetail, ApiResource.HouseVoteList],
            batch.Jobs.Select(job => job.Definition.ApiResource!.Value));
        Assert.Equal(HouseVoteJobKeys.NextPage(HouseVoteFixtures.SourceId, "list-job"), batch.Jobs[^1].IdempotencyKey);
    }

    [Fact]
    public void TheLastPageOfAnEarlierSessionHandsOverToTheFirstPageOfTheFollowingSession()
    {
        var page = new HouseVoteListPage(HouseVoteFixtures.FirstSession, [Item(1)], Next: null);

        var batch = Configured().CreateHouseVoteListBatch("list-job", page, Today);

        var handover = batch.Jobs[^1];
        Assert.Equal(Source + "/119/2?format=json&limit=250", handover.Definition.Url);
        Assert.Equal(ApiResource.HouseVoteList, handover.Definition.ApiResource);
        Assert.Equal(HouseVoteJobKeys.NextPage(HouseVoteFixtures.SourceId, "list-job"), handover.IdempotencyKey);
    }

    [Fact]
    public void TheLastPageOfTheCurrentSessionEndsTheSweep()
    {
        var page = new HouseVoteListPage(new HouseVoteSession(119, 2), [Item(1, session: 2)], Next: null);

        var batch = Configured().CreateHouseVoteListBatch("list-job", page, Today);

        Assert.Equal([ApiResource.HouseVoteDetail], batch.Jobs.Select(job => job.Definition.ApiResource!.Value));
    }

    [Fact]
    public void TheMembersJobOfAVoteIsKeyedByThePlaceAndVersionOfTheVote()
    {
        var job = Configured().CreateHouseVoteMembersJob(HouseVoteFixtures.Vote(100, HouseVoteFixtures.Updated), Today);

        Assert.Equal(Source + "/119/1/100/members?format=json", job.Definition.Url);
        Assert.Equal(ApiResource.HouseVoteMembers, job.Definition.ApiResource);
        Assert.Equal(HouseVoteJobKeys.Members(HouseVoteFixtures.SourceId, HouseVoteFixtures.Vote(100, HouseVoteFixtures.Updated)), job.IdempotencyKey);
    }

    [Fact]
    public void ABillSourceRefusesHouseVoteAdmission()
    {
        var bills = new ConfiguredCollectionSource(CongressBillSourceConfigurationTests.Configuration(), "congress-bills", HouseVoteFixtures.Backfill);

        Assert.Throws<ArgumentException>(() => bills.CreateHouseVoteListBatch("job",
            new HouseVoteListPage(HouseVoteFixtures.FirstSession, [], null), Today));
        Assert.Throws<ArgumentException>(() => bills.CreateHouseVoteMembersJob(HouseVoteFixtures.Vote(1, HouseVoteFixtures.Updated), Today));
    }

    private static HouseVoteListItem Item(int roll, int session = 1) =>
        new(new HouseVoteRoll(new HouseVoteSession(119, session), roll), HouseVoteFixtures.Updated);

    private static ConfiguredCollectionSource Configured() =>
        new(HouseVoteFixtures.Configuration(), HouseVoteFixtures.SourceId, HouseVoteFixtures.Backfill);
}
