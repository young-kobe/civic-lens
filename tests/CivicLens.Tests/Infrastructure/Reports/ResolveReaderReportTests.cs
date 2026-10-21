using CivicLens.Application.Reports;
using CivicLens.Core.Review;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Reports;
using CivicLens.Tests.Infrastructure.Collection;
using static CivicLens.Tests.Publication.Contracts.PulledReportTests;

namespace CivicLens.Tests.Infrastructure.Reports;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ResolveReaderReportTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly ReviewActor Reviewer = new("auth0|friend", ReviewRole.Reviewer);

    private string connectionString = null!;
    private PostgresReaderReportStore store = null!;
    private ResolveReaderReport resolve = null!;
    private Guid reportId;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = new PostgresReaderReportStore(AnalysisDbContextFactory.Create(connectionString));
        resolve = new ResolveReaderReport(store);
        var report = Report();
        reportId = report.ReportId;
        await store.SavePulledAsync(Batch(report), default);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ACorrectionNeedsANoteSoTheRecordPageCanTellTheEditorWhatToFix()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ResolveAs(Reviewer, ReaderReportStatus.CorrectionNeeded, null));
        await Assert.ThrowsAsync<ArgumentException>(() => ResolveAs(Reviewer, ReaderReportStatus.CorrectionNeeded, "  "));
        Assert.Equal(1, await store.CountOpenAsync(default));

        await ResolveAs(Reviewer, ReaderReportStatus.CorrectionNeeded, "  Fix the quote.  ");

        Assert.Equal("Fix the quote.", (await store.GetAsync(reportId, default))!.Report.Note);
    }

    [Fact]
    public async Task ADismissalNeedsNoNoteAndABlankNoteIsStoredAsNone()
    {
        await ResolveAs(Reviewer, ReaderReportStatus.Dismissed, "   ");

        var report = (await store.GetAsync(reportId, default))!.Report;
        Assert.Equal((ReaderReportStatus.Dismissed, null), (report.Status, report.Note));
    }

    [Theory]
    [InlineData(ReaderReportStatus.Open)]
    [InlineData((ReaderReportStatus)9)]
    public async Task OnlyDismissedOrCorrectionNeededResolvesAReport(ReaderReportStatus status)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ResolveAs(Reviewer, status, "Note."));
    }

    [Fact]
    public async Task OnlyAReviewerOrTheOwnerResolvesAndTheCodeSubjectCannotSoNoMachineClosesAReaderReport()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ResolveAs(new(ReviewAuthor.CodeSubject, ReviewRole.Owner), ReaderReportStatus.Dismissed, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ResolveAs(new("", ReviewRole.Reviewer), ReaderReportStatus.Dismissed, null));
        Assert.Equal(1, await store.CountOpenAsync(default));

        await ResolveAs(new("auth0|owner", ReviewRole.Owner), ReaderReportStatus.Dismissed, null);

        Assert.Equal(0, await store.CountOpenAsync(default));
    }

    [Fact]
    public async Task AResolvedReportCannotBeResolvedAgainThroughTheHandler()
    {
        await ResolveAs(Reviewer, ReaderReportStatus.Dismissed, null);

        await Assert.ThrowsAsync<CivicLens.Application.Review.ReviewConflictException>(() => ResolveAs(Reviewer, ReaderReportStatus.CorrectionNeeded, "Late."));
    }

    private Task ResolveAs(ReviewActor actor, ReaderReportStatus status, string? note) =>
        resolve.ExecuteAsync(actor, new(reportId, 0, status, note));
}
