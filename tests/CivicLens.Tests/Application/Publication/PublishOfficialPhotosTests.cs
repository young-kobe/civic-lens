using System.Text;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishOfficialPhotosTests
{
    private static readonly string FirstHash = new('a', 64);
    private static readonly string SecondHash = new('b', 64);
    private static readonly string MayorPage = PublicationProtocol.OfficialPagePath("mayor");

    [Fact]
    public async Task APublishWritesTheCapturedImageUnderItsHashAndTheProfileNamesIt()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first image"));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", Vote(scenario, 'a')));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Equal("first image", Encoding.UTF8.GetString(files[PublicationProtocol.PhotoPath(FirstHash)]));
        var photo = Officials(files).Officials.Single(profile => profile.Id == "mayor").Photo!;
        Assert.Equal(PublicationProtocol.PhotoPath(FirstHash), photo.Path);
        Assert.Equal(MemberPhoto.MemberGuideCredit, photo.Credit);
        Assert.Equal((225, 275), (photo.Width, photo.Height));
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
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first image"), ("mayor", "attempt-2", SecondHash, "second image"));
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
    public async Task AnUnchangedPhotoIsLinkedFromThePreviousReleaseAndItsCaptureIsNotReadAgain()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first image"));
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Vote(scenario, 'a')));
        scenario.Releases.WriteOrder.Clear();
        scenario.Releases.Links.Clear();

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        Assert.Equal(["attempt-1"], scenario.Captures.Reads);
        Assert.Contains((first.DirectoryName, PublicationProtocol.PhotoPath(FirstHash)), scenario.Releases.Links);
        Assert.DoesNotContain(PublicationProtocol.PhotoPath(FirstHash), scenario.Releases.WriteOrder);
        Assert.Equal("first image", Encoding.UTF8.GetString(scenario.Releases.Directories[second.DirectoryName][PublicationProtocol.PhotoPath(FirstHash)]));
    }

    [Fact]
    public async Task AnUnchangedPhotoNeedsNoArtifactDirectoryBecauseNoCaptureIsRead()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first image"));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k1", Vote(scenario, 'a')));
        scenario.ArtifactRoot = null;

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k2", scenario.AddDraft('c')));

        Assert.Contains(PublicationProtocol.PhotoPath(FirstHash), scenario.Releases.Directories[second.DirectoryName].Keys);
    }

    [Fact]
    public async Task TwoOfficialsWithOneImageShareOneFileSoTheFileIsWrittenAndReadOnce()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "shared image"), ("chair", "attempt-2", FirstHash, "shared image"));

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            PublicationScenario.Request("k", Vote(scenario, 'a'),
                new PublishedRecordReference(PublishedRecordKind.Statement, scenario.AddStatement('b', speakerIds: ["chair"]))));

        Assert.Single(scenario.Releases.WriteOrder, PublicationProtocol.PhotoPath(FirstHash));
        Assert.Single(scenario.Captures.Reads);
        var officials = Officials(scenario.Releases.Directories[summary.DirectoryName]).Officials;
        Assert.All(officials, profile => Assert.Equal(PublicationProtocol.PhotoPath(FirstHash), profile.Photo!.Path));
    }

    [Fact]
    public async Task ANewPhotoWithNoArtifactDirectoryFailsWithAClearMessageAndWritesNoRelease()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, "first image"));
        scenario.ArtifactRoot = null;

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", Vote(scenario, 'a'))));

        Assert.Contains("The new photo of mayor needs the artifact directory", failure.Message, StringComparison.Ordinal);
        Assert.Empty(scenario.Releases.Directories);
    }

    [Fact]
    public async Task AnImageOverTheByteLimitOfThePhotoSourceFailsThePublish()
    {
        var scenario = ScenarioWith(("mayor", "attempt-1", FirstHash, new string('x', (int)MemberPhotoFixtures.Source().MaxBytes + 1)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k", Vote(scenario, 'a'))));
    }

    private static PublicationScenario ScenarioWith(params (string PersonId, string AttemptId, string Hash, string Image)[] photos)
    {
        var scenario = new PublicationScenario();
        scenario.ExternalIds["mayor"] = ("A000001", null);
        scenario.ExternalIds["chair"] = ("A000002", null);
        foreach (var photo in photos) scenario.Captures.Add(photo.AttemptId, Encoding.UTF8.GetBytes(photo.Image));
        scenario.Photos = [.. photos.GroupBy(photo => photo.PersonId).Select(group => group.First()).Select(photo => PhotoRow(photo.PersonId, photo.AttemptId, photo.Hash))];
        return scenario;
    }

    private static OfficialPhoto PhotoRow(string personId, string attemptId, string hash) =>
        new(personId, MemberPhotoFixtures.PhotoUrl(), MemberPhoto.MemberGuideCredit, attemptId, hash);

    private static PublishedRecordReference Vote(PublicationScenario scenario, char id) => new(PublishedRecordKind.Vote, scenario.AddVote(id));

    private static PublishedOfficials Officials(Dictionary<string, byte[]> files) =>
        JsonSerializer.Deserialize<PublishedOfficials>(files[PublicationProtocol.OfficialsPath], PublicationProtocol.JsonOptions)!;
}
