using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;
using CivicLens.Tests.Application.Analysis;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Actions;

public sealed class BackfillActionCodeTextTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    [Fact]
    public async Task ARecordThatTheWorkerRedraftedFromTheBillTextAfterTheListWasReadKeepsItsNewText()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var drafted = await CodeRevisionAsync(world) with
        {
            AuthorSubject = ReviewAuthor.AnalysisSubject,
            TextOrigin = ReviewTextOrigin.Model,
            BillCitation = null,
            TextPackageId = ActionBillTexts.PackageId
        };

        var (result, store) = await BackfillAsync(world, drafted);

        Assert.Equal((1, 0, ActionCodeTextSkip.NotRetiredText), (result.Examined, result.Moved, Assert.Single(result.Skips).Reason));
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task ARecordThatAnotherRunAlreadyMovedGetsNoSecondRevision()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var moved = await CodeRevisionAsync(world);

        var (result, store) = await BackfillAsync(world, moved);

        Assert.Equal((0, ActionCodeTextSkip.NotRetiredText), (result.Moved, Assert.Single(result.Skips).Reason));
        Assert.Empty(store.Revisions);
    }

    [Fact]
    public async Task ARetiredTitleOnlyDraftStillMovesToCodeText()
    {
        var world = ActionBillWorld.Create(summaries: []);
        var retired = await CodeRevisionAsync(world) with
        {
            AuthorSubject = ReviewAuthor.AnalysisSubject,
            TextOrigin = ReviewTextOrigin.Model,
            BillCitation = null,
            Summary = "No bill summary is available in the record, so this description relies on the bill title alone."
        };

        var (result, store) = await BackfillAsync(world, retired);

        Assert.Equal((1, 0), (result.Moved, result.Skipped));
        Assert.Equal(ReviewTextOrigin.Code, Assert.Single(store.Revisions).TextOrigin);
    }

    /// <summary>The code revision that the worker writes for this bill, with no model call because no policy area issue is asked.</summary>
    private static async Task<ActionDraftRevision> CodeRevisionAsync(ActionBillWorld world)
    {
        var store = new FakeActionDraftStore(world.Evidence());
        await AnalysisWorkers.ActionDrafting(store, new FakeAnalysisStatusStore(), world.Reader(), new FakeHeadlineSummaryModel().Draft(
                System.Text.Json.JsonSerializer.Serialize(new { issueIds = new[] { "rent" }, confidence = 0.8 })),
            new ActionDraftSettings(10_000_000, concurrency: 1), BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(once: true);
        return Assert.Single(store.Revisions);
    }

    private static async Task<(ActionCodeTextBackfillResult Result, FakeActionDraftStore Store)> BackfillAsync(ActionBillWorld world,
        ActionDraftRevision current)
    {
        var store = new FakeActionDraftStore(world.Evidence(current));
        var result = await new BackfillActionCodeText(store, world.Reader(), BillGateFixtures.Revision(), TimeProvider.System).ExecuteAsync(Owner, 10, null);
        return (result, store);
    }
}
