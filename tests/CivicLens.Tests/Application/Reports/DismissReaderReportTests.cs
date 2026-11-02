using CivicLens.Application.Reports;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Application.Reports;

public sealed class DismissReaderReportTests
{
    private static readonly ReviewActor Reviewer = new("auth0|friend", ReviewRole.Reviewer);
    private static readonly ReviewActor CodeSubject = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    private readonly FakeReaderReportStore store = new();
    private readonly Guid reportId = Guid.NewGuid();

    [Fact]
    public async Task OnlyAReviewerOrTheOwnerResolvesAndTheCodeSubjectCannotSoNoMachineClosesAReaderReport()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Dismiss(CodeSubject));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Dismiss(new("", ReviewRole.Reviewer)));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Close(CodeSubject));
        Assert.Empty(store.Dismissals);
        Assert.Empty(store.Closures);

        await Dismiss(new("auth0|owner", ReviewRole.Owner));

        Assert.Equal(("auth0|owner", reportId, 3), Assert.Single(store.Dismissals));
    }

    [Fact]
    public async Task ASaveThatCorrectsNoReportAsksTheStoreForNothing()
    {
        await new CloseCorrectedReports(store).ExecuteAsync(Reviewer, PublishedRecordKind.Vote, "id", []);

        Assert.Empty(store.Closures);
    }

    [Fact]
    public async Task AClosedCorrectionCarriesTheFixedNoteBecauseNoTypedNoteMayReachAnyText()
    {
        await Close(Reviewer);

        Assert.Equal(CloseCorrectedReports.Note, Assert.Single(store.Closures).Note);
    }

    private Task Dismiss(ReviewActor actor) => new DismissReaderReport(store).ExecuteAsync(actor, reportId, 3);

    private Task Close(ReviewActor actor) =>
        new CloseCorrectedReports(store).ExecuteAsync(actor, PublishedRecordKind.Vote, "id", [reportId]);
}
