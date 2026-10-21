using CivicLens.Application.Reports;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Reports;

public sealed class ResolveReaderReportTests
{
    private static readonly ReviewActor Reviewer = new("auth0|friend", ReviewRole.Reviewer);

    private readonly FakeReaderReportStore store = new();
    private readonly Guid reportId = Guid.NewGuid();

    [Fact]
    public async Task ACorrectionNeedsANoteSoTheRecordPageCanTellTheEditorWhatToFix()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ResolveAs(Reviewer, ReaderReportStatus.CorrectionNeeded, null));
        await Assert.ThrowsAsync<ArgumentException>(() => ResolveAs(Reviewer, ReaderReportStatus.CorrectionNeeded, "  "));
        Assert.Empty(store.Resolutions);

        await ResolveAs(Reviewer, ReaderReportStatus.CorrectionNeeded, "  Fix the quote.  ");

        Assert.Equal("Fix the quote.", Assert.Single(store.Resolutions).Note);
    }

    [Fact]
    public async Task ADismissalNeedsNoNoteAndABlankNoteIsStoredAsNone()
    {
        await ResolveAs(Reviewer, ReaderReportStatus.Dismissed, "   ");

        var resolution = Assert.Single(store.Resolutions);
        Assert.Equal((ReaderReportStatus.Dismissed, null), (resolution.Status, resolution.Note));
    }

    [Theory]
    [InlineData(ReaderReportStatus.Open)]
    [InlineData((ReaderReportStatus)9)]
    public async Task OnlyDismissedOrCorrectionNeededResolvesAReport(ReaderReportStatus status)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ResolveAs(Reviewer, status, "Note."));
        Assert.Empty(store.Resolutions);
    }

    [Fact]
    public async Task ANoteOverTheLimitOrWithANulCharacterIsRefusedBecauseTheDatabaseCannotStoreIt()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ResolveAs(Reviewer, ReaderReportStatus.Dismissed, new string('x', ResolveReaderReport.MaximumNoteCharacters + 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => ResolveAs(Reviewer, ReaderReportStatus.Dismissed, "a\0b"));
        Assert.Empty(store.Resolutions);
    }

    [Fact]
    public async Task OnlyAReviewerOrTheOwnerResolvesAndTheCodeSubjectCannotSoNoMachineClosesAReaderReport()
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ResolveAs(new(ReviewAuthor.CodeSubject, ReviewRole.Owner), ReaderReportStatus.Dismissed, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ResolveAs(new("", ReviewRole.Reviewer), ReaderReportStatus.Dismissed, null));
        Assert.Empty(store.Resolutions);

        await ResolveAs(new("auth0|owner", ReviewRole.Owner), ReaderReportStatus.Dismissed, null);

        Assert.Equal("auth0|owner", Assert.Single(store.Resolutions).Actor);
    }

    private Task ResolveAs(ReviewActor actor, ReaderReportStatus status, string? note) =>
        new ResolveReaderReport(store).ExecuteAsync(actor, new(reportId, 0, status, note));
}
