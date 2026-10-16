using CivicLens.Application.Briefs;
using CivicLens.Core.Review;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Application.Briefs;

public sealed class RedraftBriefTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    [Fact]
    public async Task OnlyTheOwnerMayRedraftAWeek()
    {
        var store = new FakeBriefDraftStore(BriefFixtures.Evidence([]));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            new RedraftBrief(store).ExecuteAsync(new("auth0|reviewer", ReviewRole.Reviewer), BriefFixtures.Week));
        Assert.Empty(store.Redrafted);
    }

    [Fact]
    public async Task AWeekThatDoesNotStartOnAMondayIsRefusedBeforeTheStoreIsCalled()
    {
        var store = new FakeBriefDraftStore(BriefFixtures.Evidence([]));

        await Assert.ThrowsAsync<ArgumentException>(() => new RedraftBrief(store).ExecuteAsync(Owner, BriefFixtures.Week.AddDays(1)));
        Assert.Empty(store.Redrafted);
    }

    [Fact]
    public async Task TheOwnerRedraftsAMondayAndGetsTheStoreCountBack()
    {
        var store = new FakeBriefDraftStore(BriefFixtures.Evidence([]));

        var requeued = await new RedraftBrief(store).ExecuteAsync(Owner, BriefFixtures.Week);

        Assert.Equal(1, requeued);
        Assert.Equal([BriefFixtures.Week], store.Redrafted);
    }
}
