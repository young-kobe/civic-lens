using System.Text;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Publication;

public sealed class PublishCorrectionsTests
{
    private static readonly ReviewActor Code = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    [Fact]
    public async Task APersonReplacementWithoutANoteIsRefusedBecauseAReaderMustSeeWhyTheRecordChanged()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);

        await Assert.ThrowsAsync<MissingCorrectionNoteException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));

        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task APersonReplacementWithANoteWritesAnEntryThatNamesTheReplacedRevision()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);
        scenario.Clock.Now = scenario.Clock.Now.AddDays(3);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action, "  Fixed the issue.  "));

        var entry = Assert.Single(ReadAction(scenario, second.DirectoryName, action).Corrections);
        Assert.Equal((1, scenario.Clock.Now, "Fixed the issue."), (entry.ReplacedRevisionNumber, entry.PublishedAtUtc, entry.Note));
    }

    [Fact]
    public async Task ASecondCorrectionKeepsTheFirstEntryBelowTheNewOne()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action, "First fix."));
        scenario.ReviseAction(action, 3);

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k3", action, "Second fix."));

        var corrections = ReadAction(scenario, third.DirectoryName, action).Corrections;
        Assert.Equal([(2, "Second fix."), (1, "First fix.")], corrections.Select(item => (item.ReplacedRevisionNumber, item.Note)));
    }

    [Fact]
    public async Task APersonPublishOverTheNotedCapFailsLoudInsteadOfDroppingTheOldestEntry()
    {
        var (scenario, action) = await WithNotedCorrectionsAsync(PublishedCorrection.MaximumNotedEntries);
        scenario.ReviseAction(action, PublishedCorrection.MaximumNotedEntries + 2);

        await Assert.ThrowsAsync<CorrectionLimitException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("over", action, "One too many.")));
    }

    [Fact]
    public async Task ARunOfCodeUpdatesStaysOneEntryThatNamesTheNewestReplacedRevision()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);
        await scenario.Publisher().ExecuteAsync(Code, Request("k2", action));
        scenario.ReviseAction(action, 3);
        scenario.Clock.Now = scenario.Clock.Now.AddDays(1);

        var third = await scenario.Publisher().ExecuteAsync(Code, Request("k3", action));

        var entry = Assert.Single(ReadAction(scenario, third.DirectoryName, action).Corrections);
        Assert.Equal((2, scenario.Clock.Now, null), (entry.ReplacedRevisionNumber, entry.PublishedAtUtc, entry.Note));
    }

    [Fact]
    public async Task ACodeUpdateAfterAPersonCorrectionAddsOneEntryAndKeepsTheCorrection()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action, "Fix."));
        scenario.ReviseAction(action, 3);

        var third = await scenario.Publisher().ExecuteAsync(Code, Request("k3", action));

        var corrections = ReadAction(scenario, third.DirectoryName, action).Corrections;
        Assert.Equal([(2, null), (1, "Fix.")], corrections.Select(item => (item.ReplacedRevisionNumber, item.Note)));
    }

    [Fact]
    public async Task ACodeUpdateOfARecordAtTheNotedCapStillPublishesSoCodeFactsNeverStall()
    {
        var (scenario, action) = await WithNotedCorrectionsAsync(PublishedCorrection.MaximumNotedEntries);
        scenario.ReviseAction(action, PublishedCorrection.MaximumNotedEntries + 2);

        var next = await scenario.Publisher().ExecuteAsync(Code, Request("code", action));

        var corrections = ReadAction(scenario, next.DirectoryName, action).Corrections;
        Assert.Equal(PublishedCorrection.MaximumNotedEntries + 1, corrections.Length);
        Assert.Null(corrections[0].Note);
    }

    [Fact]
    public async Task AWindowsLineBreakCountsAsOneCharacterSoTheBrowserLimitAndTheServerAgree()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);
        var note = "a\r\n" + new string('b', PublishedCorrection.MaximumNoteLength - 2);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action, note));

        Assert.Equal(note.Replace("\r\n", "\n"), Assert.Single(ReadAction(scenario, second.DirectoryName, action).Corrections).Note);
    }

    [Fact]
    public async Task ADefaultNoteListIsRefusedBeforeAnythingIsBuilt()
    {
        var (scenario, action) = await PublishedActionAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Request("k2", action) with { Notes = default }));
    }

    [Fact]
    public async Task ANoteOnAFirstPublicationIsRefusedBecauseNothingWasCorrected()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action, "No change yet.")));

        Assert.Empty(scenario.Publications.Committed);
    }

    [Fact]
    public async Task ANoteForARecordOutsideTheRequestIsRefusedSoNoNoteIsLostSilently()
    {
        var (scenario, action) = await PublishedActionAsync();
        var other = scenario.AddAction('d');
        scenario.ReviseAction(action, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Request("k2", action, "Fix.") with { Notes = [Note(action, "Fix."), Note(other, "Stray.")] }));
    }

    [Fact]
    public async Task TwoNotesForOneRecordAreRefusedBecauseTheEntryHoldsOne()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Request("k2", action) with { Notes = [Note(action, "One."), Note(action, "Two.")] }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\0note")]
    public async Task ABlankOrNulNoteIsRefused(string note)
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action, note)));
    }

    [Fact]
    public async Task ANoteOverTheContractLengthIsRefusedAndTheLimitLengthIsAccepted()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Request("k2", action, new string('a', PublishedCorrection.MaximumNoteLength + 1))));
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner,
            Request("k3", action, new string('a', PublishedCorrection.MaximumNoteLength)));
    }

    [Fact]
    public async Task TheCodeSubjectWritesAnEntryWithoutANoteBecauseItOnlyRefreshesFromTheSource()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);

        var second = await scenario.Publisher().ExecuteAsync(Code, Request("k2", action));

        var entry = Assert.Single(ReadAction(scenario, second.DirectoryName, action).Corrections);
        Assert.Equal((1, null), (entry.ReplacedRevisionNumber, entry.Note));
    }

    [Fact]
    public async Task TheCodeSubjectCannotSendANoteBecauseOnlyAPersonCorrects()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);

        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(Code, Request("k2", action, "Fix.")));
    }

    [Fact]
    public async Task TheSameKeyWithADifferentNoteIsAPayloadMismatchAndTheSameNotesReplay()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);
        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action, "Fix."));

        var replay = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action, "Fix."));

        Assert.Equal(first, replay);
        await Assert.ThrowsAsync<ArgumentException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action, "Other.")));
    }

    [Fact]
    public async Task ACarriedRecordKeepsItsEntriesWhenALaterPublishRendersItsPageAgain()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action, "Fix."));
        var statement = scenario.AddStatement('b');
        var relationship = scenario.AddRelationship('e', statement, action, targetRevision: 2);

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, PublicationScenario.Request("k3",
            new PublishedRecordReference(PublishedRecordKind.Statement, statement),
            new PublishedRecordReference(PublishedRecordKind.Relationship, relationship)));

        var page = Encoding.UTF8.GetString(scenario.Releases.Directories[third.DirectoryName][PublicationProtocol.RecordPagePath(action)]);
        Assert.Equal($"<html>action {action} related e corrections 1</html>", page);
        Assert.Single(ReadAction(scenario, third.DirectoryName, action).Corrections);
    }

    private static async Task<(PublicationScenario Scenario, string Action)> PublishedActionAsync()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k1", action));
        return (scenario, action);
    }

    private static async Task<(PublicationScenario Scenario, string Action)> WithNotedCorrectionsAsync(int count)
    {
        var (scenario, action) = await PublishedActionAsync();
        for (var revision = 2; revision <= count + 1; revision++)
        {
            scenario.ReviseAction(action, revision);
            await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request($"k{revision}", action, "Fix."));
        }
        return (scenario, action);
    }

    private static RecordNote Note(string actionId, string text) => new(PublishedRecordKind.Action, actionId, text);

    private static PublishReleaseRequest Request(string key, string actionId, string? note = null)
    {
        var request = PublicationScenario.Request(key, new PublishedRecordReference(PublishedRecordKind.Action, actionId));
        return note is null ? request : request with { Notes = [Note(actionId, note)] };
    }

    private static PublishedAction ReadAction(PublicationScenario scenario, string directory, string actionId) =>
        JsonSerializer.Deserialize<PublishedAction>(
            scenario.Releases.Directories[directory][PublicationProtocol.RecordDataPath(actionId)], PublicationProtocol.JsonOptions)!;
}
