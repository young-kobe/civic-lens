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
    public void TwoVoteviewSourcesCannotBeEnabledTogetherBecauseBothWouldKeyRecordsOnTheSameRollCalls()
    {
        var configuration = VoteviewVoteFixtures.Configuration();
        var second = VoteviewVoteFixtures.Source("voteview-second", enabled: true, url: VoteviewVoteFixtures.SourceUrl + "/second");

        var error = Assert.Throws<ArgumentException>((configuration with { Sources = [.. configuration.Sources, second] }).Validate);
        Assert.Contains("only one Senate vote source", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AStoredRevisionWithTheRetiredSenateGovSourceStillValidatesWhetherItIsEnabledOrNot()
    {
        var configuration = VoteviewVoteFixtures.Configuration();

        (configuration with { Sources = [.. configuration.Sources, VoteviewVoteFixtures.RetiredSenateGovSource(enabled: false)] }).Validate();
        (configuration with { Sources = [.. configuration.Sources, VoteviewVoteFixtures.RetiredSenateGovSource(enabled: true)] }).Validate();
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
    public void TheShippedVoteviewSourceIsOffByDefaultAndTheShippedFileHasNoSenateGovSource()
    {
        var shipped = JsonSerializer.Deserialize<CollectionConfiguration>(
            File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "federal-coverage.json")), ConfigurationJson.Options)!;
        shipped.Validate();
        var voteview = shipped.Sources.Single(candidate => candidate.Id == VoteviewVoteFixtures.SourceId);
        Assert.False(voteview.Enabled);
        Assert.DoesNotContain(shipped.Sources, source => source.ApiParser == ApiParser.SenateVoteList);
    }
}
