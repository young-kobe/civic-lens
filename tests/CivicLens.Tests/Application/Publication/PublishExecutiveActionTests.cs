using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishExecutiveActionTests
{
    private static readonly ReviewActor Code = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    [Fact]
    public async Task TheEntryNamesThePresidentDatesTheRecordBySigningAndCarriesNoIssue()
    {
        var scenario = new PublicationScenario();
        var record = scenario.AddExecutiveAction('a');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", record));

        var entry = Assert.Single(ReadManifest(scenario, summary.DirectoryName).Records);
        Assert.Equal(PublishedRecordKind.ExecutiveAction, entry.Kind);
        Assert.Equal("Ending the Weaponization of the Federal Government", entry.Headline);
        Assert.Equal(["mayor"], entry.Officials.Select(official => official.Id));
        Assert.Empty(entry.IssueIds);
        Assert.Empty(entry.PolicyAreaIds);
        Assert.Equal("www.federalregister.gov", entry.SourceHost);
        Assert.Equal(new DateOnly(2025, 1, 20), entry.EventDate);
    }

    [Fact]
    public async Task TheRecordCitesEachFactAndEmbedsTheSavedRecordOnce()
    {
        var scenario = new PublicationScenario();
        var record = scenario.AddExecutiveAction('a');

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", record));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        Assert.Contains(PublicationProtocol.RecordPagePath(record), files.Keys);
        var published = JsonSerializer.Deserialize<PublishedExecutiveAction>(files[PublicationProtocol.RecordDataPath(record)], PublicationProtocol.JsonOptions)!;
        Assert.Single(published.Sources);
        Assert.Equal(PublishedExecutiveActionKind.ExecutiveOrder, published.Kind);
        Assert.Equal("Mayor Example", published.President.Name);
        Assert.All(published.Facts, fact => Assert.Equal($"{fact.Label}: {fact.Value}", fact.Citation.Quote));
        Assert.Equal(PublicationScenario.ApprovedAt, published.ApprovedAtUtc);
    }

    [Fact]
    public async Task ACorrectionNoticeIsPublishedAsAFactThatEqualsTheContractNoticeValue()
    {
        var scenario = new PublicationScenario();
        var record = scenario.AddExecutiveAction('a', withNotice: true);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", record));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        var published = JsonSerializer.Deserialize<PublishedExecutiveAction>(files[PublicationProtocol.RecordDataPath(record)], PublicationProtocol.JsonOptions)!;
        published.Validate();
        var notice = Assert.Single(published.CorrectionNotices);
        var fact = Assert.Single(published.Facts, item => item.Label == PublishedExecutiveAction.CorrectionNoticeLabel);
        Assert.Equal(notice.Value, fact.Value);
        Assert.Equal(2, published.Sources.Length);
    }

    [Fact]
    public async Task DispositionNotesPublishAsOneFullCitedFactWithTheirExactMultilineSpan()
    {
        const string notes = "Keep the original record.\r\n\U00010400 remains in context.";
        var scenario = new PublicationScenario();
        var record = scenario.AddExecutiveAction('a', dispositionNotes: notes);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", record));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        var published = JsonSerializer.Deserialize<PublishedExecutiveAction>(files[PublicationProtocol.RecordDataPath(record)], PublicationProtocol.JsonOptions)!;
        published.Validate();
        var fact = Assert.Single(published.Facts, item => item.Label == "Disposition notes");
        var source = Assert.Single(published.Sources);
        var quote = $"Disposition notes: {notes}";
        Assert.Equal(notes, fact.Value);
        Assert.Equal(quote, fact.Citation.Quote);
        Assert.Equal(source.ExtractionId, fact.Citation.ExtractionId);
        Assert.Equal(source.Text.IndexOf(quote, StringComparison.Ordinal), fact.Citation.Start);
        Assert.Equal(quote.Length, fact.Citation.Length);
        Assert.Equal(source.Text.IndexOf(notes, StringComparison.Ordinal), source.Text.LastIndexOf(notes, StringComparison.Ordinal));
    }

    [Fact]
    public async Task CorrectionNoticeKeepsDispositionNotesBoundToTheOriginalSource()
    {
        const string notes = "Original disposition.\r\n\U00010400 remains here.";
        var scenario = new PublicationScenario();
        var record = scenario.AddExecutiveAction('a', withNotice: true, dispositionNotes: notes);

        var summary = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", record));

        var files = scenario.Releases.Directories[summary.DirectoryName];
        var published = JsonSerializer.Deserialize<PublishedExecutiveAction>(files[PublicationProtocol.RecordDataPath(record)], PublicationProtocol.JsonOptions)!;
        published.Validate();
        var fact = Assert.Single(published.Facts, item => item.Label == "Disposition notes");
        var original = Assert.Single(published.Sources, source => source.Text.Contains(notes, StringComparison.Ordinal));
        Assert.Equal(notes, fact.Value);
        Assert.Equal(original.ExtractionId, fact.Citation.ExtractionId);
        Assert.Equal(2, published.Sources.Length);
        Assert.Contains(published.Sources, source => source.ExtractionId != original.ExtractionId);
        Assert.Single(published.Facts, item => item.Label == PublishedExecutiveAction.CorrectionNoticeLabel);
    }

    [Fact]
    public async Task CommitBindsTheRecordToItsKindSoTheStoreChecksTheExecutiveActionTable()
    {
        var scenario = new PublicationScenario();
        var record = scenario.AddExecutiveAction('a');

        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", record));

        var commit = scenario.Publications.LastCommit!;
        Assert.Equal(new PublishedRevisionBinding(PublishedRecordKind.ExecutiveAction, record, 1), Assert.Single(commit.Records));
        Assert.Equal(new ReviewStateExpectation(PublishedRecordKind.ExecutiveAction, record, 1, 1), Assert.Single(commit.AddedRecords));
    }

    [Fact]
    public async Task ARecordWhoseApprovalAPersonWithdrewIsRefused()
    {
        var scenario = new PublicationScenario();
        var record = scenario.AddExecutiveAction('a', decisions: []);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", record)));

        Assert.Equal($"Executive action {record} is not approved or has unresolved concerns.", error.Message);
        Assert.Equal(0, scenario.Releases.BeginCount);
    }

    [Fact]
    public async Task TheWorkerPublishesAnExecutiveActionWhoeverApprovedIt()
    {
        var scenario = new PublicationScenario();
        var byCode = scenario.AddExecutiveAction('a', [PublicationScenario.Decision(new string('a', 64), 1, ReviewDecisionKind.Approve) with { ActorSubject = ReviewAuthor.CodeSubject }], unpublished: true);
        var byPerson = scenario.AddExecutiveAction('b', unpublished: true);
        var worker = new PublishApprovedRecords(scenario.Publisher(), scenario.Publications, scenario.Loaders(),
            new CapturingLogger<PublishApprovedRecords>(), TestBeat.Create(), TimeProvider.System, new CountingWakeup());
        scenario.Publications.BeforeServe = () =>
        {
            var published = scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId).ToHashSet();
            scenario.ExecutiveActions.Unpublished.RemoveAll(item => published.Contains(item.Action.RecordId));
        };

        await worker.ExecuteAsync(default);

        Assert.Equal([byCode, byPerson], scenario.Publications.LastCommit!.AddedRecords.Select(added => added.RecordId).Order());
        Assert.Empty(scenario.ExecutiveActions.Unpublished);
    }

    private static PublishReleaseRequest Request(string key, string recordId) =>
        PublicationScenario.Request(key, new PublishedRecordReference(PublishedRecordKind.ExecutiveAction, recordId));

    private static PublicationRelease ReadManifest(PublicationScenario scenario, string directory) =>
        JsonSerializer.Deserialize<PublicationRelease>(
            scenario.Releases.Directories[directory][PublicationProtocol.ManifestPath], PublicationProtocol.JsonOptions)!;

}
