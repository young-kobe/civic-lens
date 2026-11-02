using CivicLens.Application.Reports;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Reports;

public sealed class DismissReaderReportTests
{
    private static readonly ReviewActor CodeSubject = new(ReviewAuthor.CodeSubject, ReviewRole.Owner);

    private readonly FakeReaderReportStore store = new();
    private readonly Guid reportId = Guid.NewGuid();

    [Fact]
    public async Task OnlyAReviewerOrTheOwnerResolvesAndTheCodeSubjectCannotSoNoMachineClosesAReaderReport()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Dismiss(CodeSubject));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Dismiss(new("", ReviewRole.Reviewer)));
        Assert.Empty(store.Dismissals);

        await Dismiss(new("auth0|owner", ReviewRole.Owner));

        Assert.Equal(("auth0|owner", reportId, 3), Assert.Single(store.Dismissals));
    }

    private Task Dismiss(ReviewActor actor) => new DismissReaderReport(store).ExecuteAsync(actor, reportId, 3);
}
