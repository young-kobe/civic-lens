using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.MemberPhotos;
using CivicLens.Collection.Contracts;
using System.Text.Json;
using CivicLens.Application.Configuration;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Collection.MemberPhotos;

public sealed class MemberPhotoSourceTests
{
    private static readonly DateOnly Today = new(2026, 10, 28);

    [Fact]
    public void ThePhotoUrlNamesTheBioguideIdAndReadsBackToIt()
    {
        var url = MemberPhotoUrl.Photo(MemberPhotoFixtures.SourceUrl, "A000001");

        Assert.Equal("https://unitedstates.github.io/images/congress/225x275/A000001.jpg", url);
        Assert.Equal("A000001", MemberPhotoUrl.TryReadBioguideId(MemberPhotoFixtures.SourceUrl, url));
    }

    [Theory]
    [InlineData("https://unitedstates.github.io/images/congress/450x550/A000001.jpg")]
    [InlineData("https://unitedstates.github.io/images/congress/225x275/A000001.png")]
    [InlineData("https://unitedstates.github.io/images/congress/225x275/a000001.jpg")]
    [InlineData("https://unitedstates.github.io/images/congress/225x275/nested/A000001.jpg")]
    [InlineData("https://unitedstates.github.io/images/congress/225x275/A000001.jpg?x=1")]
    public void AUrlThatIsNotAPhotoOfABioguideIdUnderTheSourceReadsAsNone(string url) =>
        Assert.Null(MemberPhotoUrl.TryReadBioguideId(MemberPhotoFixtures.SourceUrl, url));

    [Fact]
    public void AMemberPhotoJobIsBoundToAPhotoUrlUnderTheSourcePrefixAndToNoOtherUrlOrResource()
    {
        var source = new ConfiguredCollectionSource(MemberPhotoFixtures.Configuration(), MemberPhotoFixtures.SourceId, Today);

        var definition = source.CreateApiJobDefinition(ApiResource.MemberPhoto, MemberPhotoFixtures.PhotoUrl(), Today, CollectionJobPriority.Backfill);

        Assert.Equal(MemberPhotoFixtures.PhotoUrl(), definition.Url);
        Assert.Throws<ArgumentException>(() =>
            source.CreateApiJobDefinition(ApiResource.MemberPhoto, MemberPhotoFixtures.SourceUrl, Today, CollectionJobPriority.Backfill));
        Assert.Throws<ArgumentException>(() =>
            source.CreateApiJobDefinition(ApiResource.MemberPhoto, MemberPhotoFixtures.PhotoUrl() + "?x=1", Today, CollectionJobPriority.Backfill));
        Assert.Throws<ArgumentException>(() =>
            source.CreateApiJobDefinition(ApiResource.MemberRoster, MemberPhotoFixtures.PhotoUrl(), Today, CollectionJobPriority.Backfill));
    }

    [Fact]
    public void TheRosterSourceRejectsAPhotoUrlSoOnlyThePhotoSourceFetchesPhotos()
    {
        var source = new ConfiguredCollectionSource(MemberPhotoFixtures.Configuration(), RosterFixtures.SourceId, Today);

        Assert.Throws<ArgumentException>(() =>
            source.CreateApiJobDefinition(ApiResource.MemberPhoto, MemberPhotoFixtures.PhotoUrl(), Today, CollectionJobPriority.Backfill));
    }

    [Fact]
    public void ThePhotoSourceValidatesCoversNoPeopleAndStaysEligible()
    {
        var configuration = MemberPhotoFixtures.Configuration(RosterFixtures.Person("a", "A", "A000001"));
        var source = configuration.Sources.Last();

        configuration.Validate();
        Assert.Empty(configuration.CoveredPersonIdsOf(source, Today));
        Assert.Equal(source, configuration.EligibleSource(source.Id, Today));
    }

    [Fact]
    public void ACredentialIsRejectedBecauseTheImagesArePublic()
    {
        var source = MemberPhotoFixtures.Source() with { Credential = CollectionCredentials.CongressGov };

        Assert.Throws<ArgumentException>(() => WithPhotoSource(source).Validate());
    }

    [Fact]
    public void AValidatorIsRejectedBecauseA304WouldLeaveACheckWithoutACapture()
    {
        Assert.Throws<ArgumentException>(() => WithPhotoSource(MemberPhotoFixtures.Source() with { ETag = "\"v1\"" }).Validate());
        Assert.Throws<ArgumentException>(() =>
            WithPhotoSource(MemberPhotoFixtures.Source() with { LastModified = DateTimeOffset.UnixEpoch }).Validate());
    }

    [Theory]
    [InlineData(CollectionMode.Page)]
    [InlineData(CollectionMode.Feed)]
    [InlineData(CollectionMode.Html)]
    public void APhotoParserOutsideApiModeIsRejectedBecauseOnlyApiModeKeepsTheRawBytes(CollectionMode mode) =>
        Assert.Throws<ArgumentException>(() => WithPhotoSource(MemberPhotoFixtures.Source() with { Mode = mode }).Validate());

    [Fact]
    public void APolicyThatCannotHoldOneJobForEachPersonWithABioguideIdIsRejectedSoTheFirstAdmissionCannotFailLate()
    {
        PersonConfiguration[] people =
        [
            RosterFixtures.Person("a", "A", "A000001"),
            RosterFixtures.Person("b", "B", "B000002"),
            RosterFixtures.Person("c", "C", null)
        ];
        var holdsTwo = MemberPhotoFixtures.Source() with { AdmissionPolicy = new() { MaxJobs = 2, MaxTotalRequests = 12 } };
        var holdsOne = MemberPhotoFixtures.Source() with { AdmissionPolicy = new() { MaxJobs = 1, MaxTotalRequests = 12 } };

        (RosterFixtures.Configuration(people) with { Sources = [holdsTwo] }).Validate();
        Assert.Throws<ArgumentException>((RosterFixtures.Configuration(people) with { Sources = [holdsOne] }).Validate);
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(0, 29, true)]
    [InlineData(0, 30, false)]
    [InlineData(990, 1019, true)]
    public void AKeyChangesEveryThirtyDaysFromTheUnixEpochSoEachPersonGetsOneJobForEachPeriod(int firstDay, int secondDay, bool sameKey)
    {
        var epoch = new DateOnly(1970, 1, 1);

        var first = MemberPhotoJobKeys.Photo("member-photos", "A000001", epoch.AddDays(firstDay));
        var second = MemberPhotoJobKeys.Photo("member-photos", "A000001", epoch.AddDays(secondDay));

        Assert.Equal(sameKey, first == second);
        Assert.StartsWith("member-photo/member-photos/A000001/", first);
    }

    [Fact]
    public void TheBatchHoldsOneBackfillJobForEachConfiguredPersonWithABioguideIdAndNoOtherPerson()
    {
        PersonConfiguration[] people =
        [
            RosterFixtures.Person("a", "A", "A000001"),
            RosterFixtures.Person("c", "C", null)
        ];
        var source = new ConfiguredCollectionSource(MemberPhotoFixtures.Configuration(people), MemberPhotoFixtures.SourceId, Today);

        var batch = source.CreateMemberPhotoBatch(Today);

        var job = Assert.Single(batch.Jobs);
        Assert.Equal((ApiResource.MemberPhoto, MemberPhotoFixtures.PhotoUrl("A000001"), CollectionJobPriority.Backfill),
            (job.Definition.ApiResource, job.Definition.Url, job.Definition.Priority));
    }

    [Fact]
    public void ThePhotoSourceMakesNoScheduledRootJobBecauseTheRosterJobCreatesThePhotoJobs()
    {
        var source = new ConfiguredCollectionSource(MemberPhotoFixtures.Configuration(), MemberPhotoFixtures.SourceId, Today);

        Assert.Null(source.CreateScheduledJobDefinition(DateTimeOffset.UtcNow, null));
    }

    [Fact]
    public void TheShippedPhotoSourceIsOffByDefaultAndItsPolicyHoldsAJobForEveryConfiguredBioguideId()
    {
        var shipped = JsonSerializer.Deserialize<CollectionConfiguration>(
            File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "federal-coverage.json")), ConfigurationJson.Options)!;
        var source = shipped.Sources.Single(candidate => candidate.ApiParser == ApiParser.MemberPhotos);
        shipped.Validate();
        Assert.False(source.Enabled);

        var enabled = shipped with { Sources = [.. shipped.Sources.Select(item => item == source ? source with { Enabled = true } : item)] };
        var batch = new ConfiguredCollectionSource(enabled, source.Id, Today).CreateMemberPhotoBatch(Today);

        Assert.Equal(shipped.People.Count(person => person.BioguideId is not null), batch.Jobs.Count);
        Assert.True(batch.Policy.CanReserve(batch.Template, batch.Jobs.Count));
    }

    private static CollectionConfiguration WithPhotoSource(WatchedSourceConfiguration source) =>
        RosterFixtures.Configuration() with { Sources = [source] };
}
