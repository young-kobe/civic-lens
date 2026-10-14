using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Issues;
using CivicLens.Collection.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Issues;

public sealed class IssueConfigurationRevisionTests
{
    [Fact]
    public void OwnerEditedIssueFileLoadsAndValidates()
    {
        var json = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "config", "issues.json"));

        var configuration = JsonSerializer.Deserialize<IssueConfiguration>(json, CollectionProtocol.JsonOptions)!;
        configuration.Validate();

        Assert.Equal(32, configuration.PolicyAreas.Length);
        Assert.NotEmpty(configuration.Issues);
        Assert.Matches("^[0-9a-f]{64}$", IssueConfigurationRevision.Create(configuration).Id);
    }

    [Fact]
    public void RevisionJsonAndIdMatchThePinnedConfigurationVersion()
    {
        var revision = IssueConfigurationRevision.Create(Configuration(Issue()));

        Assert.Equal(1, Configuration().Version);
        Assert.True(revision.Json == """{"version":1,"policyAreas":[{"id":"housing","name":"Housing"}],"issues":[{"id":"rent","name":"Rent","definition":"Statements about rent costs.","policyAreaIds":["housing"]}]}""" &&
            revision.Id == "4807efab11b3b8c60f7733c05737895699a38d5a9734f81ae584d8740f72cf79",
            "The issue snapshot format changed. Bump IssueConfiguration.Version (or version the snapshot), " +
            "update the version table in docs/architecture.md, then update this test. Actual: " + revision.Id + " " + revision.Json);
    }

    [Fact]
    public void RevisionIdChangesWhenDefinitionChangesAndIsStableOtherwise()
    {
        var original = IssueConfigurationRevision.Create(Configuration());

        Assert.Equal(original.Id, IssueConfigurationRevision.Create(Configuration()).Id);
        Assert.NotEqual(original.Id, IssueConfigurationRevision.Create(
            Configuration(Issue() with { Definition = "A reworded definition." })).Id);
    }

    [Fact]
    public void RevisionRoundTripsThroughJsonAndReadsDetachedConfiguration()
    {
        var revision = IssueConfigurationRevision.Create(Configuration(Issue() with { Includes = ["rent"] }));
        var serialized = JsonSerializer.Serialize(revision, CollectionProtocol.JsonOptions);

        var deserialized = JsonSerializer.Deserialize<IssueConfigurationRevision>(serialized, CollectionProtocol.JsonOptions);
        revision.ReadConfiguration().Issues[0].PolicyAreaIds[0] = "changed";

        Assert.Equal(revision, deserialized);
        Assert.Equal("housing", revision.ReadConfiguration().Issues[0].PolicyAreaIds[0]);
    }

    [Fact]
    public void ConstructorRejectsTamperedIdAndTamperedOrNoncanonicalJson()
    {
        var revision = IssueConfigurationRevision.Create(Configuration());
        Assert.Throws<ArgumentException>(() => new IssueConfigurationRevision(new string('0', 64), revision.Json));

        var tampered = revision.Json.Replace("Rent", "Rents", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new IssueConfigurationRevision(revision.Id, tampered));

        var spaced = revision.Json.Replace("{", "{ ", StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => new IssueConfigurationRevision(Hash(spaced), spaced));

        const string malformed = "{";
        Assert.Throws<ArgumentException>(() => new IssueConfigurationRevision(Hash(malformed), malformed));
    }

    [Fact]
    public void ConstructorRejectsUnsupportedVersion()
    {
        var json = IssueConfigurationRevision.Create(Configuration()).Json.Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal);

        Assert.Throws<ArgumentException>(() => new IssueConfigurationRevision(Hash(json), json));
    }

    [Fact]
    public void IssueMustLinkToKnownPolicyAreaSoItCanReachBills() =>
        AssertInvalid(Configuration(Issue() with { PolicyAreaIds = ["unknown"] }));

    [Fact]
    public void IssueWithoutPolicyAreaIsRejected() =>
        AssertInvalid(Configuration(Issue() with { PolicyAreaIds = [] }));

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        AssertInvalid(Configuration(Issue(), Issue()));
        AssertInvalid(Configuration() with { PolicyAreas = [Area(), Area()] });
        AssertInvalid(Configuration() with { PolicyAreas = [Area(), Area() with { Id = "other" }] });
        AssertInvalid(Configuration(Issue() with { PolicyAreaIds = ["housing", "housing"] }));
        AssertInvalid(Configuration(Issue() with { Includes = ["rent", "rent"] }));
    }

    [Theory]
    [InlineData("Bad Id")]
    [InlineData("UPPER")]
    [InlineData("-lead")]
    [InlineData("")]
    public void MalformedIssueIdIsRejected(string id) => AssertInvalid(Configuration(Issue() with { Id = id }));

    [Fact]
    public void BlankTextAndNullEntriesAreRejected()
    {
        AssertInvalid(Configuration(Issue() with { Definition = " " }));
        AssertInvalid(Configuration(Issue() with { Excludes = [" "] }));
        AssertInvalid(Configuration() with { Issues = [null!] });
    }

    [Fact]
    public void BadDateRangeIsRejected() =>
        AssertInvalid(Configuration(Issue() with { StartsOn = new DateOnly(2026, 2, 1), EndsBefore = new DateOnly(2026, 2, 1) }));

    [Fact]
    public void IssueCountAboveReviewCatalogLimitIsRejected() =>
        AssertInvalid(Configuration([.. Enumerable.Range(0, 257).Select(index => Issue() with { Id = $"issue-{index}" })]));

    private static void AssertInvalid(IssueConfiguration configuration) =>
        Assert.Throws<ArgumentException>(configuration.Validate);

    private static PolicyAreaConfiguration Area() => new() { Id = "housing", Name = "Housing" };

    private static TrackedIssueConfiguration Issue() => new()
    {
        Id = "rent",
        Name = "Rent",
        Definition = "Statements about rent costs.",
        PolicyAreaIds = ["housing"]
    };

    private static IssueConfiguration Configuration(params TrackedIssueConfiguration[] issues) =>
        new() { PolicyAreas = [Area()], Issues = issues.Length == 0 ? [Issue()] : issues };

    private static string Hash(string json) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
