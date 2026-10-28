using System.Text;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using Microsoft.Extensions.Logging;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishOfficialPhotosTests
{
    private static readonly string FirstHash = new('a', 64);
    private static readonly string SecondHash = new('b', 64);
    private static readonly string MayorPage = PublicationProtocol.OfficialPagePath("mayor");

    [Fact]
    public async Task APublishWritesTheCapturedImageUnderItsCaptureHashEvenWhenTheConfigurationHasNoPhotoSource()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first"));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", Vote(scenario, 'a')));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Equal(Image("first"), files[PublicationProtocol.PhotoPath(FirstHash)]);
        var photo = Officials(files).Officials.Single(profile => profile.Id == "mayor").Photo!;
        Assert.Equal(PublicationProtocol.PhotoPath(FirstHash), photo.Path);
        Assert.Equal(MemberPhoto.MemberGuideCredit, photo.Credit);
        Assert.Equal((225, 275), (photo.Width, photo.Height));
    }

    [Fact]
    public async Task APublishReadsThePhotoRowsOnceSoNoSecondReadCanDisagreeWithTheFirst()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first"), ("chair", "attempt-2", SecondHash, "second"));

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", Vote(scenario, 'a'),
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b', speakerIds: ["chair"]))));

        Assert.Equal(1, scenario.PhotoReads);
    }

    [Fact]
    public async Task AnOfficialWithNoPhotoRowGetsNoPhotoSoNoFileIsWritten()
    {
        var scenario = ScenarioWith();

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", Vote(scenario, 'a')));

        Assert.Null(Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials.Single(profile => profile.Id == "mayor").Photo);
        Assert.DoesNotContain(scenario.Releases.WriteOrder, path => path.StartsWith("photos/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANewPhotoRewritesTheOfficialPageBecauseTheProfileChanged()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first"), ("mayor", "attempt-2", SecondHash, "second"));
        scenario.Photos = [PhotoRow("mayor", "attempt-1", FirstHash)];
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Vote(scenario, 'a')));
        scenario.Photos = [PhotoRow("mayor", "attempt-2", SecondHash)];
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        Assert.Contains(MayorPage, scenario.Releases.WriteOrder);
        Assert.Contains(PublicationProtocol.PhotoPath(SecondHash), scenario.Releases.WriteOrder);
        Assert.DoesNotContain((first.DirectoryName, PublicationProtocol.PhotoPath(FirstHash)), scenario.Releases.Links);
        Assert.Equal(PublicationProtocol.PhotoPath(SecondHash),
            Officials(scenario.Releases.Directories[second.DirectoryName]).Officials.Single(profile => profile.Id == "mayor").Photo!.Path);
    }

    [Fact]
    public async Task APhotoThatLeavesTheProfileRewritesTheOfficialPageAndWritesNoFile()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first"));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Vote(scenario, 'a')));
        scenario.Photos = [];
        scenario.Releases.WriteOrder.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        Assert.Contains(MayorPage, scenario.Releases.WriteOrder);
        var files = scenario.Releases.Directories[second.DirectoryName];
        Assert.Null(Officials(files).Officials.Single(profile => profile.Id == "mayor").Photo);
        Assert.DoesNotContain(PublicationProtocol.PhotoPath(FirstHash), files.Keys);
    }

    [Fact]
    public async Task AnUnchangedPhotoIsLinkedFromThePreviousReleaseAndItsCaptureIsNotReadAgain()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first"));
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Vote(scenario, 'a')));
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        Assert.Equal(["attempt-1"], scenario.Captures.Reads);
        Assert.Contains((first.DirectoryName, PublicationProtocol.PhotoPath(FirstHash)), scenario.Releases.Links);
        Assert.DoesNotContain(PublicationProtocol.PhotoPath(FirstHash), scenario.Releases.WriteOrder);
        Assert.Equal(Image("first"), scenario.Releases.Directories[second.DirectoryName][PublicationProtocol.PhotoPath(FirstHash)]);
    }

    [Fact]
    public async Task AnUnchangedPhotoNeedsNoArtifactDirectoryBecauseNoCaptureIsRead()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first"));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Vote(scenario, 'a')));
        scenario.ArtifactRoot = null;

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        Assert.Contains(PublicationProtocol.PhotoPath(FirstHash), scenario.Releases.Directories[second.DirectoryName].Keys);
    }

    [Fact]
    public async Task APreviousReleaseThatLacksTheLinkedFileFailsThePublishInsteadOfDroppingThePhoto()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first"));
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Vote(scenario, 'a')));
        scenario.Releases.Directories[first.DirectoryName].Remove(PublicationProtocol.PhotoPath(FirstHash));

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c'))));
    }

    [Fact]
    public async Task TwoOfficialsWithOneCaptureShareOneFileSoTheFileIsWrittenAndReadOnce()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "shared"), ("chair", "attempt-2", FirstHash, "shared"));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", Vote(scenario, 'a'),
            new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b', speakerIds: ["chair"]))));

        Assert.Single(scenario.Releases.WriteOrder, PublicationProtocol.PhotoPath(FirstHash));
        Assert.Single(scenario.Captures.Reads);
        var officials = Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials;
        Assert.All(officials, profile => Assert.Equal(PublicationProtocol.PhotoPath(FirstHash), profile.Photo!.Path));
    }

    [Fact]
    public async Task ANewPhotoWithNoArtifactDirectoryFailsWithItsMessageAndWritesNoRelease()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first"));
        scenario.ArtifactRoot = null;

        var failure = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", Vote(scenario, 'a'))));

        Assert.Equal("The new photo of mayor needs the artifact directory to read its capture, and no artifact directory is set.", failure.Message);
        Assert.Empty(scenario.Releases.Directories);
    }

    [Fact]
    public async Task AnUnreadableNewPhotoKeepsThePhotoOfThePreviousReleaseAndLogsAnError()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first"));
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Vote(scenario, 'a')));
        scenario.Photos = [PhotoRow("mayor", "attempt-2", SecondHash)];
        scenario.Captures.AddCorrupt("attempt-2");
        scenario.Releases.Links.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        var files = scenario.Releases.Directories[second.DirectoryName];
        Assert.Equal(PublicationProtocol.PhotoPath(FirstHash), Officials(files).Officials.Single(profile => profile.Id == "mayor").Photo!.Path);
        Assert.Contains((first.DirectoryName, PublicationProtocol.PhotoPath(FirstHash)), scenario.Releases.Links);
        Assert.DoesNotContain(PublicationProtocol.PhotoPath(SecondHash), files.Keys);
        var error = Assert.Single(scenario.Captures.Log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("mayor", error.Message, StringComparison.Ordinal);
        Assert.Contains("attempt-2", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("notAPhoto")]
    public async Task AnUnreadableNewPhotoWithNoPreviousPhotoGivesNoPhotoLogsAnErrorAndStillPublishes(string fault)
    {
        var scenario = ScenarioWith();
        scenario.Photos = [PhotoRow("mayor", "attempt-1", FirstHash)];
        if (fault == "corrupt") scenario.Captures.AddCorrupt("attempt-1");
        if (fault == "notAPhoto") scenario.Captures.Add("attempt-1", Encoding.UTF8.GetBytes("not an image"));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", Vote(scenario, 'a')));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Null(Officials(files).Officials.Single(profile => profile.Id == "mayor").Photo);
        Assert.DoesNotContain(files.Keys, path => path.StartsWith("photos/", StringComparison.Ordinal));
        var error = Assert.Single(scenario.Captures.Log.Entries, entry => entry.Level == LogLevel.Error);
        Assert.Contains("mayor", error.Message, StringComparison.Ordinal);
        Assert.Contains("attempt-1", error.Message, StringComparison.Ordinal);
    }

    private static PublicationScenario ScenarioWith(params (string PersonId, string AttemptId, string Hash, string Tag)[] photos)
    {
        var scenario = new PublicationScenario();
        foreach (var photo in photos) scenario.Captures.Add(photo.AttemptId, Image(photo.Tag));
        scenario.Photos = [.. photos.GroupBy(photo => photo.PersonId).Select(group => group.First()).Select(photo => PhotoRow(photo.PersonId, photo.AttemptId, photo.Hash))];
        return scenario;
    }

    private static byte[] Image(string tag) => [.. MemberPhotoFixtures.Jpeg(), .. Encoding.UTF8.GetBytes(tag)];

    private static OfficialPhoto PhotoRow(string personId, string attemptId, string hash) =>
        new(personId, MemberPhotoFixtures.PhotoUrl(), MemberPhoto.MemberGuideCredit, attemptId, hash);

    private static PublishedRecordReference Vote(PublicationScenario scenario, char id) => new(PublishedRecordKind.Vote, scenario.AddVote(id));

    private static PublishedOfficials Officials(Dictionary<string, byte[]> files) =>
        JsonSerializer.Deserialize<PublishedOfficials>(files[PublicationProtocol.OfficialsPath], PublicationProtocol.JsonOptions)!;
}
