using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.PublicApi;

namespace CivicLens.Tests.Application.Publication;

/// <summary>A retained release whose files, manifest, officials file, and stored projections all agree. A test breaks one of them.</summary>
internal sealed record BillCountReleaseFixture(PublicationReleaseSummary Release, PublishedAction Action, PublishedVote Vote)
{
    public static readonly DateOnly EventDay = new(2026, 1, 3);

    public static BillCountReleaseFixture Add(PublicationScenario scenario, int releaseNumber)
    {
        var sponsor = (PublishedAction)PublishedSamples.Action("Headline", "Summary.", "A bill title", "Sponsor text for the bill.").Body;
        var cosponsor = sponsor.Sponsorships[0] with
        {
            Official = new PublishedOfficial { Id = "o2", Name = "Official o2" },
            Role = PublishedSponsorshipRole.Cosponsor,
            IsOriginal = false
        };
        var action = sponsor with { Sponsorships = [sponsor.Sponsorships[0], cosponsor] };
        var vote = (PublishedVote)PublishedSamples.Vote("Vote headline", "Vote summary.", "Vote source text.").Body;
        var release = new PublicationReleaseSummary(releaseNumber, $"{releaseNumber:D6}-{new string('a', 32)}",
            DateTimeOffset.UnixEpoch.AddDays(releaseNumber), 2);
        var fixture = new BillCountReleaseFixture(release, action, vote);
        scenario.Publications.BillCountReleases.Add(release);
        scenario.Publications.BillCountSources[releaseNumber] = fixture.Source();
        scenario.Releases.Directories[release.DirectoryName] = fixture.Files();
        return fixture;
    }

    public PublishedRevisionBinding ActionBinding => new(PublishedRecordKind.Action, Action.RecordId, Action.RevisionNumber);

    public PublishedRevisionBinding VoteBinding => new(PublishedRecordKind.Vote, Vote.RecordId, Vote.RevisionNumber);

    public ReleaseBillCountSource Source() => new([ActionBinding, VoteBinding],
        [new(Action.RecordId, Action.RevisionNumber, Action.Congress, EventDay, ["o1"], ["o2"])],
        [new(Vote.RecordId, Vote.RevisionNumber, Vote.Congress)], ["o1", "o2"]);

    public Dictionary<string, byte[]> Files(PublishedAction? action = null, PublicationRelease? manifest = null, string[]? officials = null)
    {
        var shownAction = action ?? Action;
        manifest ??= new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = Release.ReleaseNumber,
            PublishedAtUtc = Release.PublishedAtUtc,
            Records = [Vote.ToEntry(), shownAction.ToEntry()],
            Withdrawals = []
        };
        return new()
        {
            [PublicationProtocol.ManifestPath] = Json(manifest),
            [PublicationProtocol.OfficialsPath] = Json(new PublishedOfficials
            {
                Officials = [.. (officials ?? ["o1", "o2"]).Select(id => new PublishedOfficialProfile { Id = id, Name = $"Official {id}", Terms = [] })]
            }),
            [PublicationProtocol.RecordDataPath(shownAction.RecordId)] = Json(shownAction),
            [PublicationProtocol.RecordDataPath(Vote.RecordId)] = Json(Vote)
        };
    }

    private static byte[] Json<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, PublicationProtocol.JsonOptions);
}
