using System.Text;
using System.Text.Json;
using System.Collections.Immutable;
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
        scenario.ReviseAction(action, 2, correctionNote: null);

        await Assert.ThrowsAsync<MissingCorrectionNoteException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));

        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task APersonReplacementWithANoteWritesAnEntryThatNamesTheReplacedRevision()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, correctionNote: "Fixed the issue.");
        scenario.Clock.Now = scenario.Clock.Now.AddDays(3);

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action));

        var entry = Assert.Single(ReadAction(scenario, second.DirectoryName, action).Corrections);
        Assert.Equal((1, scenario.Clock.Now, "Fixed the issue."), (entry.ReplacedRevisionNumber, entry.PublishedAtUtc, entry.Note));
    }

    [Fact]
    public async Task ASecondCorrectionKeepsTheFirstEntryBelowTheNewOne()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, correctionNote: "First fix.");
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action));
        scenario.ReviseAction(action, 3, correctionNote: "Second fix.");

        var third = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k3", action));

        var corrections = ReadAction(scenario, third.DirectoryName, action).Corrections;
        Assert.Equal([(2, "Second fix."), (1, "First fix.")], corrections.Select(item => (item.ReplacedRevisionNumber, item.Note)));
    }

    [Fact]
    public async Task APersonPublishOverTheNotedCapFailsLoudInsteadOfDroppingTheOldestEntry()
    {
        var (scenario, action) = await WithNotedCorrectionsAsync(PublishedCorrection.MaximumNotedEntries);
        scenario.ReviseAction(action, PublishedCorrection.MaximumNotedEntries + 2, correctionNote: "One too many.");

        await Assert.ThrowsAsync<CorrectionLimitException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("over", action)));
    }

    [Fact]
    public async Task ARunOfCodeUpdatesStaysOneEntryThatNamesTheNewestReplacedRevision()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, ReviewAuthor.CodeSubject);
        await scenario.Publisher().ExecuteAsync(Code, Request("k2", action));
        scenario.ReviseAction(action, 3, ReviewAuthor.CodeSubject);
        scenario.Clock.Now = scenario.Clock.Now.AddDays(1);

        var third = await scenario.Publisher().ExecuteAsync(Code, Request("k3", action));

        var entry = Assert.Single(ReadAction(scenario, third.DirectoryName, action).Corrections);
        Assert.Equal((2, scenario.Clock.Now, null), (entry.ReplacedRevisionNumber, entry.PublishedAtUtc, entry.Note));
    }

    [Fact]
    public async Task ACodeUpdateAfterAPersonCorrectionAddsOneEntryAndKeepsTheCorrection()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, correctionNote: "Fix.");
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action));
        scenario.ReviseAction(action, 3, ReviewAuthor.CodeSubject);

        var third = await scenario.Publisher().ExecuteAsync(Code, Request("k3", action));

        var corrections = ReadAction(scenario, third.DirectoryName, action).Corrections;
        Assert.Equal([(2, null), (1, "Fix.")], corrections.Select(item => (item.ReplacedRevisionNumber, item.Note)));
    }

    [Fact]
    public async Task ACodeUpdateOfARecordAtTheNotedCapStillPublishesSoCodeFactsNeverStall()
    {
        var (scenario, action) = await WithNotedCorrectionsAsync(PublishedCorrection.MaximumNotedEntries);
        scenario.ReviseAction(action, PublishedCorrection.MaximumNotedEntries + 2, ReviewAuthor.CodeSubject);

        var next = await scenario.Publisher().ExecuteAsync(Code, Request("code", action));

        var corrections = ReadAction(scenario, next.DirectoryName, action).Corrections;
        Assert.Equal(PublishedCorrection.MaximumNotedEntries + 1, corrections.Length);
        Assert.Null(corrections[0].Note);
    }

    [Fact]
    public async Task AnApprovalThatWasWithdrawnAndGivenAgainWithoutANoteNeedsANoteAgainBecauseTheNewestApprovalDecides()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, correctionNote: "Fix.");
        var publishable = scenario.Actions.Publishable[action];
        scenario.Actions.Publishable[action] = publishable with
        {
            Review = publishable.Review with
            {
                Decisions = publishable.Review.Decisions
                    .Add(PublicationScenario.Decision(action, 3, ReviewDecisionKind.WithdrawApproval, 2))
                    .Add(PublicationScenario.Decision(action, 4, ReviewDecisionKind.Approve, 2))
            }
        };

        await Assert.ThrowsAsync<MissingCorrectionNoteException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));
    }

    [Fact]
    public async Task ANoteOnAnOlderRevisionIsNotUsedForANewerRevisionBecauseTheNoteBelongsToItsApproval()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, correctionNote: "Note for revision 2.");
        scenario.ReviseAction(action, 3, correctionNote: null);

        await Assert.ThrowsAsync<MissingCorrectionNoteException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));
    }

    [Fact]
    public async Task ACarriedRecordFileWithoutACorrectionListFailsLoudInsteadOfBindingToNull()
    {
        var (scenario, action) = await PublishedActionAsync();
        var files = scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName];
        var path = PublicationProtocol.RecordDataPath(action);
        files[path] = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(files[path]).Replace("\"corrections\":[]", "\"other\":1"));
        scenario.ReviseAction(action, 2);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));

        Assert.Equal("A carried record has no correction list.", error.Message);
    }

    [Fact]
    public async Task ACodeApprovalWritesAnEntryWithoutANoteBecauseItOnlyRefreshesFromTheSource()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, ReviewAuthor.CodeSubject);

        var second = await scenario.Publisher().ExecuteAsync(Code, Request("k2", action));

        var entry = Assert.Single(ReadAction(scenario, second.DirectoryName, action).Corrections);
        Assert.Equal((1, null), (entry.ReplacedRevisionNumber, entry.Note));
    }

    [Fact]
    public async Task APersonApprovalThatReplacesAPublishedRecordNeedsANoteEvenWhenTheCodeSubjectPublishesIt()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, "reviewer", correctionNote: null);

        var exception = await Assert.ThrowsAsync<MissingCorrectionNoteException>(() =>
            scenario.Publisher().ExecuteAsync(Code, Request("k2", action)));

        Assert.Contains($"action:{action}", exception.Message);
        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task ANoteOnAFirstPublicationIsDroppedBecauseNothingWasCorrected()
    {
        var scenario = new PublicationScenario();
        var action = scenario.AddAction('c', ImmutableArray.Create(PublicationScenario.Decision(new string('c', 64), 1, ReviewDecisionKind.Approve) with { CorrectionNote = "No change yet." }));

        var first = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k", action));

        Assert.Empty(ReadAction(scenario, first.DirectoryName, action).Corrections);
    }

    [Fact]
    public async Task ACarriedRecordKeepsItsEntriesWhenALaterPublishRendersItsPageAgain()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2);
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action));
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
            scenario.ReviseAction(action, revision, correctionNote: "Fix.");
            await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request($"k{revision}", action));
        }
        return (scenario, action);
    }

    private static PublishReleaseRequest Request(string key, string actionId) =>
        PublicationScenario.Request(key, new PublishedRecordReference(PublishedRecordKind.Action, actionId));

    private static PublishedAction ReadAction(PublicationScenario scenario, string directory, string actionId) =>
        JsonSerializer.Deserialize<PublishedAction>(
            scenario.Releases.Directories[directory][PublicationProtocol.RecordDataPath(actionId)], PublicationProtocol.JsonOptions)!;
}
