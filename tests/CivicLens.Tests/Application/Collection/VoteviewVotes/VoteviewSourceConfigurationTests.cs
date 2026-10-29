using CivicLens.Application.Collection;
using System.Text.Json;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Configuration;
using CivicLens.Application.Collection.VoteviewVotes;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.VoteviewVotes;

public sealed class VoteviewSourceConfigurationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ATwoSenateVoteSourcesCannotBeEnabledTogetherBecauseBothWouldKeyRecordsOnTheSameRollCalls()
    {
        var configuration = VoteviewVoteFixtures.Configuration();
        var senate = SenateVoteFixtures.Configuration().Sources.Single() with { Enabled = true };

        Assert.Throws<ArgumentException>((configuration with { Sources = [.. configuration.Sources, senate] }).Validate);
    }

    [Fact]
    public void ADisabledSenateVoteSourceMayStayConfiguredBesideTheEnabledOneForASwitchBack()
    {
        var configuration = VoteviewVoteFixtures.Configuration();
        var senate = SenateVoteFixtures.Configuration().Sources.Single() with { Enabled = false };

        (configuration with { Sources = [.. configuration.Sources, senate] }).Validate();
    }

    [Fact]
    public void AVoteviewSourceRefusesACredentialBecauseItReadsPublicFiles()
    {
        var configuration = VoteviewVoteFixtures.Configuration();

        Assert.Throws<ArgumentException>((configuration with
        {
            Sources = [configuration.Sources.Single() with { Credential = CollectionCredentials.CongressGov }]
        }).Validate);
    }

    [Fact]
    public void TheDailySweepStartsWithTheRollCallFileOfTheBackfillCongress()
    {
        var job = new ConfiguredCollectionSource(VoteviewVoteFixtures.Configuration(), VoteviewVoteFixtures.SourceId)
            .CreateScheduledJobDefinition(Now, null);

        Assert.Equal((ApiResource.VoteviewRollCalls, VoteviewUrl.File(VoteviewVoteFixtures.SourceUrl, ApiResource.VoteviewRollCalls, 119)),
            (job!.ApiResource, job.Url));
    }

    [Fact]
    public void AJobOfThisSourceMayNameOnlyAFileOfThisSource()
    {
        var configured = new ConfiguredCollectionSource(VoteviewVoteFixtures.Configuration(), VoteviewVoteFixtures.SourceId);
        var other = VoteviewUrl.File(VoteviewVoteFixtures.SourceUrl, ApiResource.VoteviewMembers, 119);

        Assert.Throws<ArgumentException>(() =>
            configured.CreateApiJobDefinition(ApiResource.VoteviewRollCalls, other, new DateOnly(2026, 10, 29), CollectionJobPriority.Check).Validate());
    }

    [Fact]
    public void TheShippedVoteviewSourceIsOffByDefaultAndCannotBeEnabledWhileTheSenateGovSourceIsOn()
    {
        var shipped = JsonSerializer.Deserialize<CollectionConfiguration>(
            File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "federal-coverage.json")), ConfigurationJson.Options)!;
        shipped.Validate();
        var voteview = shipped.Sources.Single(candidate => candidate.Id == VoteviewVoteFixtures.SourceId);
        Assert.False(voteview.Enabled);

        var both = shipped with
        {
            Sources = [.. shipped.Sources.Select(source => source.ApiParser is ApiParser.SenateVoteList or ApiParser.VoteviewSenateVoteList
                ? source with { Enabled = true }
                : source)]
        };

        Assert.Throws<ArgumentException>(both.Validate);
    }
}
