using CivicLens.Application.Collection;

namespace CivicLens.Tests.Application.Collection;

public sealed class PacingGroupConfigurationTests
{
    [Theory]
    [InlineData("house.gov", "https://mcdowell.house.gov", true)]
    [InlineData("house.gov", "https://house.gov", true)]
    [InlineData("house.gov", "https://www.mcdowell.house.gov", true)]
    [InlineData("house.gov", "https://evilhouse.gov", false)]
    [InlineData("house.gov", "https://house.gov.example.test", false)]
    [InlineData("house.gov", "https://senate.gov", false)]
    public void SuffixMatchesOnlyWholeLabelsSoLookalikeHostsStayUngrouped(string suffix, string origin, bool expected)
    {
        var configuration = Configuration(Group(suffix, 3000));

        Assert.Equal(expected, configuration.PacingGroupOf(origin) is not null);
    }

    [Fact]
    public void LongestMatchingSuffixWinsSoEachOriginHasOneGroup()
    {
        var configuration = Configuration(Group("gov", 1000), Group("house.gov", 3000), Group("ca.house.gov", 5000));

        Assert.Equal("ca.house.gov", configuration.PacingGroupOf("https://x.ca.house.gov")!.HostSuffix);
        Assert.Equal("house.gov", configuration.PacingGroupOf("https://x.house.gov")!.HostSuffix);
    }

    [Fact]
    public void HostMatchingIgnoresCase()
    {
        Assert.NotNull(Configuration(Group("house.gov", 3000)).PacingGroupOf("https://MCDOWELL.House.GOV"));
    }

    [Theory]
    [InlineData("gov", 3000)]
    [InlineData("House.gov", 3000)]
    [InlineData(".house.gov", 3000)]
    [InlineData("*.house.gov", 3000)]
    [InlineData("https://house.gov", 3000)]
    [InlineData("house.gov:443", 3000)]
    [InlineData("house.gov", 0)]
    [InlineData("house.gov", 3_600_001)]
    public void InvalidGroupsAreRejectedBecauseTheyWouldSilentlyMisgroupOrigins(string suffix, int delay)
    {
        Assert.Throws<ArgumentException>(() => Configuration(Group(suffix, delay)).Validate());
    }

    [Fact]
    public void DuplicateSuffixesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Configuration(Group("house.gov", 3000), Group("house.gov", 4000)).Validate());
    }

    [Fact]
    public void OmittedGroupsLeaveCanonicalJsonUnchangedSoOlderRevisionIdsHold()
    {
        var withoutGroups = CollectionConfigurationRevision.Create(Configuration());

        Assert.DoesNotContain("pacingGroups", withoutGroups.Json);
        Assert.Contains("pacingGroups", CollectionConfigurationRevision.Create(Configuration(Group("house.gov", 3000))).Json);
        Assert.Equal(withoutGroups.Json, CollectionConfigurationRevision.Create(withoutGroups.ReadConfiguration()).Json);
    }

    [Fact]
    public void InitialOffsetIsDeterministicAndCappedAtOneHourOrTheInterval()
    {
        var sixHourly = Source("member-about", 21600);
        var minutely = Source("member-about", 60);

        Assert.Equal(sixHourly.InitialDueOffset(), Source("member-about", 21600).InitialDueOffset());
        Assert.NotEqual(sixHourly.InitialDueOffset(), Source("member-issues", 21600).InitialDueOffset());
        var offsets = Enumerable.Range(0, 200).Select(index => Source($"s{index}", 21600).InitialDueOffset()).ToArray();
        Assert.All(offsets, offset => Assert.InRange(offset, TimeSpan.Zero, TimeSpan.FromSeconds(3599)));
        Assert.True(offsets.Max() > TimeSpan.FromMinutes(50));
        Assert.True(minutely.InitialDueOffset() < TimeSpan.FromSeconds(60));
    }

    private static WatchedSourceConfiguration Source(string id, int intervalSeconds) => new()
    {
        Id = id,
        Url = "https://example.test/" + id,
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/",
        CheckIntervalSeconds = intervalSeconds
    };

    private static PacingGroupConfiguration Group(string suffix, int delay) =>
        new() { HostSuffix = suffix, MinDelayMilliseconds = delay };

    private static CollectionConfiguration Configuration(params PacingGroupConfiguration[] groups) => new()
    {
        People = [new PersonConfiguration { Id = "person", Name = "Person" }],
        Sources = [new WatchedSourceConfiguration
        {
            Id = "source",
            Coverage = [new SourceCoverageConfiguration { PersonId = "person" }],
            Url = "https://example.test/page",
            AllowedOrigin = "https://example.test",
            AllowedPathPrefix = "/"
        }],
        PacingGroups = groups.Length == 0 ? null : groups
    };
}
