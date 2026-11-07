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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheBackfillDerivesThePinnedBodyForTypedAndLegacyRowsAndPreservesEveryOtherValue(bool legacy)
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        var original = await ActionRevisionSnapshotAsync();
        await ExecuteAsync(connectionString, "UPDATE action_revisions SET crs_summary_text = NULL, crs_summary_read = false");
        if (legacy) await MakeActionRevisionsLegacyAsync(1);

        var result = await BackfillActionRevisionsAsync(1);

        Assert.Equal((1, 1, 0), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(original, await ActionRevisionSnapshotAsync());
        Assert.Equal((0, 0, 0), await RerunCrsBackfillAsync());
        var verification = await VerifyActionRevisionsAsync();
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Fact]
    public async Task TheBackfillMarksALegitimateNullBodyCompleteAndDoesNotRetryIt()
    {
        var candidate = await CandidateWithoutSummariesAsync();
        await SettleAsync(candidate, Revision(candidate, 1));
        await ExecuteAsync(connectionString, "UPDATE action_revisions SET crs_summary_read = false");
        Assert.Equal(1, (await VerifyActionRevisionsAsync()).Legacy);

        var result = await BackfillActionRevisionsAsync(1);

        Assert.Equal((1, 1, 0), (result.Examined, result.Moved, result.Skipped));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM action_revisions WHERE crs_summary_read AND crs_summary_text IS NULL"));
        Assert.Equal((0, 0, 0), await RerunCrsBackfillAsync());
        var verification = await VerifyActionRevisionsAsync();
        Assert.Equal((1, 0, 0), (verification.Checked, verification.Legacy, verification.Mismatched));
    }

    [Theory]
    [InlineData("NEW.crs_summary_text := 'Wrong stored body';")]
    [InlineData("NEW.crs_summary_read := false;")]
    public async Task ADerivedValueThatChangesInStorageRollsBackOnlyThatRowAndCanBeRetried(string change)
    {
        await DraftAsync(UpdatedAt, DraftableOutput);
        await ReopenAsync();
        await SaveAsync(1, SuggestionChoice.Keep, "crs-backfill-edit");
        var original = await ActionRevisionSnapshotAsync();
        await ExecuteAsync(connectionString, "UPDATE action_revisions SET crs_summary_text = NULL, crs_summary_read = false");
        await ExecuteAsync(connectionString, $$"""
            CREATE FUNCTION alter_backfilled_crs() RETURNS trigger LANGUAGE plpgsql AS $function$
            BEGIN
                IF NEW.revision_number = 1 THEN {{change}} END IF;
                RETURN NEW;
            END;
            $function$;
            CREATE TRIGGER alter_backfilled_crs BEFORE UPDATE ON action_revisions
                FOR EACH ROW EXECUTE FUNCTION alter_backfilled_crs();
            """);

        var result = await BackfillActionRevisionsAsync(10);

        Assert.Equal((2, 1, 1), (result.Examined, result.Moved, result.Skipped));
        var skipped = Assert.Single(result.SkippedRevisions);
        Assert.Equal((1, "RebuildMismatch"), (skipped.RevisionNumber, skipped.Reason));
        Assert.Equal(["1|false|", "2|true|Text of <p>It funds rent aid.</p>"],
            await LinesAsync("SELECT revision_number || '|' || crs_summary_read || '|' || COALESCE(crs_summary_text, '') FROM action_revisions ORDER BY revision_number"));
        await ExecuteAsync(connectionString, "DROP TRIGGER alter_backfilled_crs ON action_revisions; DROP FUNCTION alter_backfilled_crs()");

        Assert.Equal((1, 1, 0), await RerunCrsBackfillAsync());
        Assert.Equal(original, await ActionRevisionSnapshotAsync());
        Assert.Equal((0, 0, 0), await RerunCrsBackfillAsync());
    }

    private async Task<(int Examined, int Moved, int Skipped)> RerunCrsBackfillAsync()
    {
        var result = await BackfillActionRevisionsAsync(10);
        return (result.Examined, result.Moved, result.Skipped);
    }
}
