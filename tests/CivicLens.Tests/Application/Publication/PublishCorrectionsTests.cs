using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Immutable;
using CivicLens.Application.Publication;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;

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
    public async Task ACodeApprovalThatCarriesAPersonNoteGivesTheCorrectionThatNoteBecauseTheNoteMovedToTheApprovalThatPublishes()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, ReviewAuthor.CodeSubject, correctionNote: "Clarified the date.");

        var next = await scenario.Publisher().ExecuteAsync(Code, Request("k2", action));

        var entry = Assert.Single(ReadAction(scenario, next.DirectoryName, action).Corrections);
        Assert.Equal((1, "Clarified the date."), (entry.ReplacedRevisionNumber, entry.Note));
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
    public async Task AnApprovalWithdrawnAndGivenAgainWithANotePublishesWithThatNote()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, correctionNote: null);
        var publishable = scenario.Actions.Publishable[action];
        scenario.Actions.Publishable[action] = publishable with
        {
            Review = publishable.Review with
            {
                Decisions = publishable.Review.Decisions
                    .Add(PublicationScenario.Decision(action, 3, ReviewDecisionKind.WithdrawApproval, 2))
                    .Add(PublicationScenario.Decision(action, 4, ReviewDecisionKind.Approve, 2) with { CorrectionNote = "Fixed on second approval." })
            }
        };

        var second = await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action));

        Assert.Equal("Fixed on second approval.", Assert.Single(ReadAction(scenario, second.DirectoryName, action).Corrections).Note);
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
    public async Task ALegacyRecordFileWithoutACorrectionListFailsInsteadOfFabricatingEmptyHistory()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.Publications.CorrectionHistories.Clear();
        var files = scenario.Releases.Directories[scenario.Publications.Committed[^1].DirectoryName];
        var path = PublicationProtocol.RecordDataPath(action);
        var json = JsonNode.Parse(files[path])!.AsObject();
        Assert.True(json.Remove("corrections"));
        files[path] = Encoding.UTF8.GetBytes(json.ToJsonString());
        scenario.ReviseAction(action, 2);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));
        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task AReplacementUsesTheBoundHistoryInsteadOfParsingAnObsoleteLegacyStatementFile()
    {
        var scenario = new PublicationScenario();
        var codeDecision = PublicationScenario.Decision(new string('c', 64), 1, ReviewDecisionKind.Approve) with { ActorSubject = ReviewAuthor.CodeSubject };
        var statement = scenario.AddStatement('c', [codeDecision], author: ReviewAuthor.AnalysisSubject);
        var reference = new PublishedRecordReference(PublishedRecordKind.Statement, statement);
        var first = await scenario.Publisher().ExecuteAsync(Code, PublicationScenario.Request("k1", reference));
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.RecordDataPath(statement)] = Encoding.UTF8.GetBytes("invalid old JSON");
        scenario.ReviseStatementByCode(statement, 2);

        var next = await scenario.Publisher().ExecuteAsync(Code, PublicationScenario.Request("k2", reference));

        var correction = Assert.Single(JsonSerializer.Deserialize<PublishedStatement>(
            scenario.Releases.Directories[next.DirectoryName][PublicationProtocol.RecordDataPath(statement)], PublicationProtocol.JsonOptions)!.Corrections);
        Assert.Equal((1, null), (correction.ReplacedRevisionNumber, correction.Note));
    }

    [Fact]
    public async Task ABillReplacementReadsTheExactEarlierActionFileForTheTextRewriteCheckAsAVoteReplacementDoes()
    {
        var (scenario, action) = await PublishedActionAsync();
        var first = scenario.Publications.Committed.Single();
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.RecordDataPath(action)] = Encoding.UTF8.GetBytes("invalid old JSON");
        scenario.ReviseAction(action, 2, correctionNote: "Fixed the date.");

        await Assert.ThrowsAsync<InvalidDataException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));
    }

    [Fact]
    public async Task ALegacyHistoryFileMustMatchTheExactPublishedRevision()
    {
        var (scenario, action) = await PublishedActionAsync();
        var first = scenario.Publications.Committed.Single();
        var wrong = ReadAction(scenario, first.DirectoryName, action) with { RevisionNumber = 2 };
        wrong.Validate();
        scenario.Publications.CorrectionHistories.Clear();
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.RecordDataPath(action)] =
            JsonSerializer.SerializeToUtf8Bytes(wrong, PublicationProtocol.JsonOptions);
        scenario.ReviseAction(action, 2);

        await Assert.ThrowsAsync<InvalidDataException>(() => scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, Request("k2", action)));

        Assert.Single(scenario.Publications.Committed);
    }

    [Fact]
    public async Task AStoredVoteHistoryStillRequiresTheVoteFileUsedForTextComparisonToMatchThePublishedRevision()
    {
        var (scenario, vote) = await PublishedVoteAsync();
        var first = scenario.Publications.Committed.Single();
        var wrong = ReadVote(scenario, first.DirectoryName, vote) with { RevisionNumber = 2 };
        wrong.Validate();
        scenario.Releases.Directories[first.DirectoryName][PublicationProtocol.RecordDataPath(vote)] =
            JsonSerializer.SerializeToUtf8Bytes(wrong, PublicationProtocol.JsonOptions);
        scenario.ReviseVote(vote, 2, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { Summary = "Updated summary." });

        await Assert.ThrowsAsync<InvalidDataException>(() => scenario.Publisher().ExecuteAsync(Code, VoteRequest("k2", vote)));

        Assert.Single(scenario.Publications.Committed);
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
    public async Task ACodeApprovedReplacementOfAPublishedStatementRepublishesWithAnEntryWithoutANoteLikeAnActionReplacement()
    {
        var scenario = new PublicationScenario();
        var codeDecision = PublicationScenario.Decision(new string('b', 64), 1, ReviewDecisionKind.Approve) with { ActorSubject = ReviewAuthor.CodeSubject };
        var statement = scenario.AddStatement('b', [codeDecision], author: ReviewAuthor.AnalysisSubject);
        var reference = new PublishedRecordReference(PublishedRecordKind.Statement, statement);
        await scenario.Publisher().ExecuteAsync(Code, PublicationScenario.Request("k1", reference));
        scenario.ReviseStatementByCode(statement, 2);

        var second = await scenario.Publisher().ExecuteAsync(Code, PublicationScenario.Request("k2", reference));

        var record = JsonSerializer.Deserialize<PublishedStatement>(
            scenario.Releases.Directories[second.DirectoryName][PublicationProtocol.RecordDataPath(statement)], PublicationProtocol.JsonOptions)!;
        Assert.Equal(2, record.RevisionNumber);
        var entry = Assert.Single(record.Corrections);
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

        Assert.Single(ReadAction(scenario, third.DirectoryName, action).Corrections);
    }

    [Fact]
    public async Task ACodeApprovedRevisionThatOnlyRewritesTheModelTextOfThePublishedVoteIsAModelRewrite()
    {
        var (scenario, vote) = await PublishedVoteAsync();
        scenario.ReviseVote(vote, 2, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { Headline = "New headline", Summary = "New summary." });

        var second = await scenario.Publisher().ExecuteAsync(Code, VoteRequest("k2", vote));

        var entry = Assert.Single(ReadVote(scenario, second.DirectoryName, vote).Corrections);
        Assert.Equal((1, null, PublishedTextRewrite.Model), (entry.ReplacedRevisionNumber, entry.Note, entry.TextRewrite));
    }

    [Fact]
    public async Task ARevisionThatMovesThePublishedVoteToCodeTextAndChangesNothingElseIsACodeRewrite()
    {
        var (scenario, vote) = await PublishedVoteAsync();
        scenario.ReviseVote(vote, 2, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { Headline = "The House voted on passage of H.R. 7.", Summary = "The bill passed.", TextOrigin = ReviewTextOrigin.Code });

        var second = await scenario.Publisher().ExecuteAsync(Code, VoteRequest("k2", vote));

        var entry = Assert.Single(ReadVote(scenario, second.DirectoryName, vote).Corrections);
        Assert.Equal((1, null, PublishedTextRewrite.Code), (entry.ReplacedRevisionNumber, entry.Note, entry.TextRewrite));
    }

    [Fact]
    public async Task ACodeRewriteAfterAModelRewriteReplacesItInTheRunSoTheRunHoldsOneTextRewrite()
    {
        var (scenario, vote) = await PublishedVoteAsync();
        scenario.ReviseVote(vote, 2, approverSubject: ReviewAuthor.CodeSubject, change: revision => revision with { Summary = "New summary." });
        await scenario.Publisher().ExecuteAsync(Code, VoteRequest("k2", vote));
        scenario.ReviseVote(vote, 3, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { Summary = "The bill passed.", TextOrigin = ReviewTextOrigin.Code });

        var third = await scenario.Publisher().ExecuteAsync(Code, VoteRequest("k3", vote));

        var entry = Assert.Single(ReadVote(scenario, third.DirectoryName, vote).Corrections);
        Assert.Equal((2, PublishedTextRewrite.Code), (entry.ReplacedRevisionNumber, entry.TextRewrite));
    }

    [Fact]
    public async Task ACodeRevisionThatChangesAVoteFactStaysAnUpdateFromTheSource()
    {
        var (scenario, vote) = await PublishedVoteAsync();
        scenario.ReviseVote(vote, 2, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { RollCallNumber = revision.RollCallNumber + 1 });

        var second = await scenario.Publisher().ExecuteAsync(Code, VoteRequest("k2", vote));

        var entry = Assert.Single(ReadVote(scenario, second.DirectoryName, vote).Corrections);
        Assert.Equal(PublishedTextRewrite.None, entry.TextRewrite);
    }

    [Fact]
    public async Task AFactChangeAfterATextOnlyUpdateAddsAnEntryInsteadOfHidingTheTextOnlyOne()
    {
        var (scenario, vote) = await PublishedVoteAsync();
        scenario.ReviseVote(vote, 2, approverSubject: ReviewAuthor.CodeSubject, change: revision => revision with { Summary = "New summary." });
        await scenario.Publisher().ExecuteAsync(Code, VoteRequest("k2", vote));
        scenario.ReviseVote(vote, 3, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { RollCallNumber = revision.RollCallNumber + 1 });

        var third = await scenario.Publisher().ExecuteAsync(Code, VoteRequest("k3", vote));

        var corrections = ReadVote(scenario, third.DirectoryName, vote).Corrections;
        Assert.Equal([(2, PublishedTextRewrite.None), (1, PublishedTextRewrite.Model)],
            corrections.Select(item => (item.ReplacedRevisionNumber, item.TextRewrite)));
    }

    [Fact]
    public async Task ACodeRevisionThatAlsoChangesTheIssuesStaysAnUpdateFromTheSource()
    {
        var (scenario, vote) = await PublishedVoteAsync();
        scenario.ReviseVote(vote, 2, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { Summary = "New summary.", TrackedIssueIds = ["issue-2"] });

        var second = await scenario.Publisher().ExecuteAsync(Code, VoteRequest("k2", vote));

        Assert.Equal(PublishedTextRewrite.None, Assert.Single(ReadVote(scenario, second.DirectoryName, vote).Corrections).TextRewrite);
    }

    [Fact]
    public async Task TheLabelComparesWithThePublishedRevisionAndNotWithAnUnpublishedOneBetween()
    {
        var (scenario, vote) = await PublishedVoteAsync();
        scenario.ReviseVote(vote, 2, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { RollCallNumber = revision.RollCallNumber + 1, Summary = "Two." });
        scenario.ReviseVote(vote, 3, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { Summary = "Three." });

        var third = await scenario.Publisher().ExecuteAsync(Code, VoteRequest("k3", vote));

        var entry = Assert.Single(ReadVote(scenario, third.DirectoryName, vote).Corrections);
        Assert.Equal((1, PublishedTextRewrite.None), (entry.ReplacedRevisionNumber, entry.TextRewrite));
    }

    [Fact]
    public async Task TheLabelFiresWhenAnUnpublishedRevisionChangedFactsAndTheNewRevisionRestoresThePublishedFacts()
    {
        var (scenario, vote) = await PublishedVoteAsync();
        scenario.ReviseVote(vote, 2, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { RollCallNumber = revision.RollCallNumber + 1 });
        scenario.ReviseVote(vote, 3, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { RollCallNumber = revision.RollCallNumber - 1, Summary = "Three." });

        var third = await scenario.Publisher().ExecuteAsync(Code, VoteRequest("k3", vote));

        var entry = Assert.Single(ReadVote(scenario, third.DirectoryName, vote).Corrections);
        Assert.Equal((1, PublishedTextRewrite.Model), (entry.ReplacedRevisionNumber, entry.TextRewrite));
    }

    [Fact]
    public async Task ABillMovedToCodeTextWithTheSameFactsShowsACodeRewriteWithNoVoteFactsTail()
    {
        var (scenario, action) = await PublishedActionAsync();
        // The actions text backfill appends a code approved revision with the same facts and issues and only new code text.
        scenario.ReviseAction(action, 2, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { Headline = "H.R. 7: A housing bill", Summary = "A housing bill.", TextOrigin = ReviewTextOrigin.Code });

        var second = await scenario.Publisher().ExecuteAsync(Code, Request("k2", action));

        var entry = Assert.Single(ReadAction(scenario, second.DirectoryName, action).Corrections);
        Assert.Equal((1, null, PublishedTextRewrite.Code), (entry.ReplacedRevisionNumber, entry.Note, entry.TextRewrite));
        Assert.Equal("Wording rewritten by code", PublishedCorrectionView.Title(entry));
        Assert.EndsWith(". Replaces revision 1.", PublishedCorrectionView.AfterTitle(entry, PublishedRecordKind.Action), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACodeTextRevisionThatChangesABillFactStaysAnUpdateFromTheSource()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, approverSubject: ReviewAuthor.CodeSubject,
            change: revision => revision with { Summary = "A housing bill.", TextOrigin = ReviewTextOrigin.Code, BillTitle = "A renamed housing bill" });

        var second = await scenario.Publisher().ExecuteAsync(Code, Request("k2", action));

        Assert.Equal(PublishedTextRewrite.None, Assert.Single(ReadAction(scenario, second.DirectoryName, action).Corrections).TextRewrite);
    }

    [Fact]
    public async Task AModelTextRevisionOfABillWithTheSameFactsStaysAnUpdateFromTheSource()
    {
        var (scenario, action) = await PublishedActionAsync();
        scenario.ReviseAction(action, 2, approverSubject: ReviewAuthor.CodeSubject, change: revision => revision with { Summary = "A new model summary." });

        var second = await scenario.Publisher().ExecuteAsync(Code, Request("k2", action));

        Assert.Equal(PublishedTextRewrite.None, Assert.Single(ReadAction(scenario, second.DirectoryName, action).Corrections).TextRewrite);
    }

    private static async Task<(PublicationScenario Scenario, string Vote)> PublishedVoteAsync()
    {
        var scenario = new PublicationScenario();
        var vote = scenario.AddVote('c');
        await scenario.Publisher().ExecuteAsync(PublicationScenario.Owner, VoteRequest("k1", vote));
        return (scenario, vote);
    }

    private static PublishReleaseRequest VoteRequest(string key, string voteId) =>
        PublicationScenario.Request(key, new PublishedRecordReference(PublishedRecordKind.Vote, voteId));

    private static PublishedVote ReadVote(PublicationScenario scenario, string directory, string voteId) =>
        JsonSerializer.Deserialize<PublishedVote>(
            scenario.Releases.Directories[directory][PublicationProtocol.RecordDataPath(voteId)], PublicationProtocol.JsonOptions)!;

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
