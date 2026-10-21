using CivicLens.Application.Collection;
using CivicLens.Application.Collection.SenateVotes;
using CivicLens.Application.Collection.Votes;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.SenateVotes;

public sealed class SenateVoteSourceConfigurationTests
{
    private const string Source = SenateVoteFixtures.SourceUrl;
    private const string Menu1 = Source + "/roll_call_lists/vote_menu_119_1.xml";
    private const string Vote1 = Source + "/roll_call_votes/vote1191/vote_119_1_00001.xml";
    private static readonly DateOnly Today = new(2026, 10, 21);

    [Fact]
    public void VoteSourceCoversEveryPersonWithABioguideIdFromTheBackfillDate()
    {
        var configuration = SenateVoteFixtures.Configuration();
        var source = configuration.Sources.Single();

        Assert.Equal(["with-id-a"], configuration.CoveredPersonIdsOf(source, SenateVoteFixtures.Backfill));
        Assert.Empty(configuration.CoveredPersonIdsOf(source, SenateVoteFixtures.Backfill.AddDays(-1)));
    }

    [Fact]
    public void ASenateSourceRefusesACredentialBecauseItReadsPublicFiles()
    {
        var configuration = SenateVoteFixtures.Configuration();
        var source = configuration.Sources.Single();

        Assert.Throws<ArgumentException>((configuration with
        {
            Sources = [source with { Credential = CollectionCredentials.CongressGov }]
        }).Validate);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(null, true)]
    [InlineData(119, false)]
    public void MinimumCongressAndBackfillDateAreRequiredWithTheParser(int? minimumCongress, bool hasBackfill)
    {
        var configuration = SenateVoteFixtures.Configuration();
        var source = configuration.Sources.Single() with
        {
            MinimumCongress = minimumCongress,
            BackfillFrom = hasBackfill ? SenateVoteFixtures.Backfill : null
        };

        Assert.Throws<ArgumentException>((configuration with { Sources = [source] }).Validate);
    }

    [Theory]
    [InlineData(999, 4000, 2_000_000_000, 20_000)]
    [InlineData(1000, 3999, 2_000_000_000, 20_000)]
    [InlineData(1000, 4000, 1_999_999_999, 20_000)]
    [InlineData(1000, 4000, 2_000_000_000, 19_999)]
    public void AdmissionPolicyMustHoldAFullMenuOfVoteJobsAndTheNextMenuJob(int maxJobs, int requests, long bytes, int seconds)
    {
        var configuration = SenateVoteFixtures.Configuration();
        var source = configuration.Sources.Single();
        var policy = source.AdmissionPolicy! with
        {
            MaxJobs = maxJobs,
            MaxTotalRequests = requests,
            MaxTotalBytes = bytes,
            MaxTotalTimeoutSeconds = seconds
        };

        Assert.Throws<ArgumentException>((configuration with { Sources = [source with { AdmissionPolicy = policy }] }).Validate);
        configuration.Validate();
    }

    [Theory]
    [InlineData(ApiResource.SenateVoteMenu, Menu1)]
    [InlineData(ApiResource.SenateVoteMenu, Source + "/roll_call_lists/vote_menu_119_2.xml")]
    [InlineData(ApiResource.SenateVote, Vote1)]
    [InlineData(ApiResource.SenateVote, Source + "/roll_call_votes/vote1192/vote_119_2_00659.xml")]
    public void JobsAcceptTheExactUrlFormOfTheirResourceKind(ApiResource resource, string url)
    {
        var job = Configured().CreateApiJobDefinition(resource, url, Today);

        Assert.Equal(resource, job.ApiResource);
        Assert.Null(job.Credential);
    }

    [Theory]
    [InlineData(ApiResource.SenateVote, Menu1)]
    [InlineData(ApiResource.SenateVoteMenu, Vote1)]
    [InlineData(ApiResource.SenateVote, Source + "/roll_call_votes/vote1191/vote_119_1_1.xml")]
    [InlineData(ApiResource.SenateVote, Source + "/roll_call_votes/vote1191/vote_119_1_00000.xml")]
    [InlineData(ApiResource.SenateVote, Source + "/roll_call_votes/vote1192/vote_119_1_00001.xml")]
    [InlineData(ApiResource.SenateVote, Source + "/roll_call_votes/vote1191/vote_119_3_00001.xml")]
    [InlineData(ApiResource.SenateVoteMenu, Source + "/roll_call_lists/vote_menu_119_1.xml?x=1")]
    [InlineData(ApiResource.SenateVoteMenu, Source + "/roll_call_lists/vote_menu_0119_1.xml")]
    [InlineData(ApiResource.HouseVoteList, Menu1)]
    public void AJobUrlMustHaveTheExactFormOfItsResourceSoTheWorkerNeverReadsItAsAnotherKind(ApiResource resource, string url)
    {
        Assert.Throws<ArgumentException>(() => Configured().CreateApiJobDefinition(resource, url, Today));
    }

    [Fact]
    public void ABillSourceRefusesSenateResources()
    {
        var bills = new ConfiguredCollectionSource(CongressBillSourceConfigurationTests.Configuration(), "congress-bills", Today);

        Assert.Throws<ArgumentException>(() => bills.CreateApiJobDefinition(ApiResource.SenateVote,
            "https://api.congress.gov/v3/bill/119/hr/1?format=json", Today));
        Assert.Throws<ArgumentException>(() => bills.CreateSenateMenuBatch("job", new SenateVoteMenu(SenateVoteFixtures.Session, []), Today));
    }

    [Fact]
    public void TheMenuIsARootCheckAndAVoteFileIsNot()
    {
        var configured = Configured();

        Assert.True(configured.CreateApiJobDefinition(ApiResource.SenateVoteMenu, Menu1, Today).IsRoot());
        Assert.False(configured.CreateApiJobDefinition(ApiResource.SenateVote, Vote1, Today).IsRoot());
    }

    [Fact]
    public void ADailySweepStartsAtTheMenuOfTheBackfillSession()
    {
        var job = Configured().CreateScheduledJobDefinition(new DateTimeOffset(2026, 10, 21, 3, 0, 0, TimeSpan.Zero), null);

        Assert.Equal((ApiResource.SenateVoteMenu, Menu1), (job!.ApiResource, job.Url));
    }

    [Fact]
    public void AMenuAdmitsOneJobForEachVoteAndTheMenuOfTheFollowingSession()
    {
        var menu = SenateVoteMenuParser.Parse(SenateVoteFixtures.Menu(), SenateVoteFixtures.Session);

        var batch = Configured().CreateSenateMenuBatch("menu-job", menu, Today);

        Assert.Equal([Source + "/roll_call_votes/vote1191/vote_119_1_00659.xml", Vote1,
            Source + "/roll_call_lists/vote_menu_119_2.xml"], batch.Jobs.Select(job => job.Definition.Url));
        Assert.Equal([ApiResource.SenateVote, ApiResource.SenateVote, ApiResource.SenateVoteMenu],
            batch.Jobs.Select(job => job.Definition.ApiResource!.Value));
        Assert.Equal(SenateVoteJobKeys.NextMenu(SenateVoteFixtures.SourceId, "menu-job"), batch.Jobs[^1].IdempotencyKey);
    }

    [Fact]
    public void TheMenuOfTheCurrentSessionEndsTheSweep()
    {
        var menu = new SenateVoteMenu(new VoteSession(119, 2), []);

        var batch = Configured().CreateSenateMenuBatch("menu-job", menu, Today);

        Assert.Empty(batch.Jobs);
    }

    [Fact]
    public void AVoteOfTheLast30DaysHasAKeyThatChangesEachDayAndAnOlderVoteHasOneFinalKeySoOnlyRecentVotesAreFetchedAgain()
    {
        var recent = new SenateVoteMenuItem(new SenateVoteRoll(SenateVoteFixtures.Session, 5), Today.AddDays(-30));
        var old = new SenateVoteMenuItem(new SenateVoteRoll(SenateVoteFixtures.Session, 5), Today.AddDays(-31));

        Assert.NotEqual(SenateVoteJobKeys.Vote("s", recent, Today), SenateVoteJobKeys.Vote("s", recent, Today.AddDays(1)));
        Assert.Equal(SenateVoteJobKeys.Vote("s", old, Today), SenateVoteJobKeys.Vote("s", old, Today.AddDays(1)));
        Assert.EndsWith("/2026-10-21", SenateVoteJobKeys.Vote("s", recent, Today), StringComparison.Ordinal);
    }

    [Fact]
    public void ARenderedVoteListsEveryFactTheDraftWillCiteWithMembersInOrder()
    {
        var detail = SenateVoteParser.Parse(SenateVoteFixtures.Vote(), SenateVoteFixtures.Roll1);

        var text = SenateVoteRenderer.Render(detail);

        Assert.StartsWith("Senate vote: 119 1 1\nUpdated: 2025-01-10T19:57:00Z\nStarted: 2025-01-09T14:54:00-05:00\n", text, StringComparison.Ordinal);
        Assert.Contains("\nCount: Yea 1, Nay 0, Present 0, Not voting 1\n", text, StringComparison.Ordinal);
        Assert.EndsWith("Member: S428, Angela Alsobrooks, D, MD, Yea\nMember: S429, Jim Banks, R, IN, Not Voting", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVoteTitleLineIsNotCalledTitleBecauseTheBillFactOfThatNameWouldCollideWithIt()
    {
        var detail = SenateVoteParser.Parse(SenateVoteFixtures.Vote(), SenateVoteFixtures.Roll1);

        var text = SenateVoteRenderer.Render(detail);

        Assert.Contains("\nVote title: ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\nTitle: ", text, StringComparison.Ordinal);
    }

    private static ConfiguredCollectionSource Configured() =>
        new(SenateVoteFixtures.Configuration(), SenateVoteFixtures.SourceId, SenateVoteFixtures.Backfill);
}
