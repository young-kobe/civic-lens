using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.MemberPhotos;
using CivicLens.Collection.Contracts;
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
    public void ThePhotoSourceMakesNoScheduledRootJobBecauseTheRosterJobCreatesThePhotoJobs()
    {
        var source = new ConfiguredCollectionSource(MemberPhotoFixtures.Configuration(), MemberPhotoFixtures.SourceId, Today);

        Assert.Null(source.CreateScheduledJobDefinition(DateTimeOffset.UtcNow, null));
    }

    private static CollectionConfiguration WithPhotoSource(WatchedSourceConfiguration source) =>
        RosterFixtures.Configuration() with { Sources = [source] };
}
