using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    [Fact]
    public async Task WorkerAndPersonRevisionWritersStoreTheExactCrsBodyAndKeepItsEvidenceBinding()
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        var original = (await ActionReviews().GetAsync(RecordId, default))!.Review.CurrentRevision;
        Assert.Equal("Text of <p>It funds rent aid.</p>", await ScalarAsync("SELECT crs_summary_text FROM action_revisions WHERE revision_number = 1"));
        Assert.Equal("00@2025-01-05T10:00:00Z", original.SummaryVersion);

        await ReopenAsync();
        var edited = await SaveAsync(1, SuggestionChoice.Keep, "crs-person-edit");

        Assert.Equal(original.SummaryCitation, edited.SummaryCitation);
        Assert.Equal(original.SummaryVersion, edited.SummaryVersion);
        Assert.Equal(ReviewTextOrigin.Person, edited.TextOrigin);
        Assert.Equal(["Text of <p>It funds rent aid.</p>", "Text of <p>It funds rent aid.</p>"], await LinesAsync("SELECT crs_summary_text FROM action_revisions ORDER BY revision_number"));
    }

    [Fact]
    public async Task ARevisionWithoutACrsCitationStoresNoBody()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions WHERE crs_summary_text IS NULL AND summary_version IS NULL"));
    }
}
