using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private const string TextJobKeyPattern = "bill-stream/congress-bill-text/text/%";

    private static readonly CongressBillTextVersion IntroducedText = new(BillTextFixtures.PackageId, "Introduced in House",
        new DateTimeOffset(2025, 3, 1, 5, 0, 0, TimeSpan.Zero), BillTextFixtures.CongressUrl());

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ABillWithNoSummaryGetsAVersionsJobAtDetailForEveryGateOutcomeAndItsTextStateStartsUnread(bool passed)
    {
        var detail = await EnqueueDetailAsync("detail");

        var result = await AdmitDetailWithTextAsync(detail, DetailBill(UpdatedAt, cosponsors: 0, summaries: 0), passed);

        Assert.Single(result.Jobs, job => job.Url == BillTextFixtures.VersionsUrl);
        Assert.Contains(await LinksAsync(), link => link.Key.Contains("/text/119/hr/7/") && link.DetailJobId == detail);
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT text_state FROM bill_gates"));
    }

    [Fact]
    public async Task ABillWithACongressGovSummaryGetsNoVersionsJobBecauseTheSummaryAlreadyExplainsIt()
    {
        var detail = await EnqueueDetailAsync("detail");

        var result = await AdmitDetailWithTextAsync(detail, DetailBill(UpdatedAt, cosponsors: 0, summaries: 2), passed: true);

        Assert.DoesNotContain(result.Jobs, job => job.Url.Contains("/text?"));
        Assert.Single(result.Jobs, job => job.Url.Contains("/summaries?"));
    }

    [Fact]
    public async Task ADisabledTextSourceMakesNoVersionsJob()
    {
        var detail = await EnqueueDetailAsync("detail");

        var result = await store.AdmitDetailAsync(BillsWithText(enabled: false), detail, DetailBill(UpdatedAt, cosponsors: 0),
            BillGateOutcome.Passed(BillGateReason.CoveredSponsor), BillGateFixtures.Revision().Id, await ExtractionAsync(detail), default);

        Assert.DoesNotContain(result.Jobs, job => job.Url.Contains("/text?"));
    }

    [Fact]
    public async Task AConfigurationWithoutATextSourceMakesNoVersionsJob()
    {
        var detail = await EnqueueDetailAsync("detail");

        var result = await AdmitDetailAsync(detail, DetailBill(UpdatedAt, cosponsors: 0), BillGateOutcome.Passed(BillGateReason.CoveredSponsor));

        Assert.DoesNotContain(result.Jobs, job => job.Url.Contains("/text?"));
    }

    [Fact]
    public async Task ABillWithNoFormattedTextEndsAsNoTextAndMakesNoTextJob()
    {
        var (_, versions) = await StartVersionsAsync();

        var result = await store.AdmitTextVersionsAsync(BillsWithText(), versions, null, default);

        Assert.Empty(result.Jobs);
        Assert.Equal("NoText", await ScalarAsync("SELECT text_state FROM bill_gates"));
        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '{TextJobKeyPattern}'"));
    }

    [Fact]
    public async Task TheNewestVersionAdmitsOneTextJobOnTheGovinfoOriginLinkedToTheDetailJob()
    {
        var (detail, versions) = await StartVersionsAsync();

        var result = await store.AdmitTextVersionsAsync(BillsWithText(), versions, IntroducedText, default);

        var admitted = Assert.Single(result.Jobs);
        var job = (await store.GetAsync(admitted.JobId, default))!;
        Assert.Equal((ApiResource.BillText, BillTextFixtures.PackageUrl(), "https://api.govinfo.gov", $"bill-stream/congress-bill-text/text/{versions}"),
            (job.Definition.ApiResource, job.Definition.Url, job.Definition.AllowedOrigin, job.IdempotencyKey));
        Assert.Contains(await LinksAsync(), link => link.Key.EndsWith("/text/" + versions, StringComparison.Ordinal) && link.DetailJobId == detail);
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT text_state FROM bill_gates"));
    }

    [Fact]
    public async Task ReadingTheSameVersionsPageTwiceAdmitsOneTextJob()
    {
        var (_, versions) = await StartVersionsAsync();
        await store.AdmitTextVersionsAsync(BillsWithText(), versions, IntroducedText, default);

        var again = await store.AdmitTextVersionsAsync(BillsWithText(), versions, IntroducedText, default);

        Assert.Empty(again.Jobs);
        Assert.Equal(1, again.DuplicateCount);
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '{TextJobKeyPattern}'"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AVersionsReadWithoutAnEnabledTextSourceFailsAsInvalidDataAndAdmitsNothing(bool configured)
    {
        var (_, versions) = await StartVersionsAsync();
        var source = configured ? BillsWithText(enabled: false) : Source();

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitTextVersionsAsync(source, versions, IntroducedText, default));

        Assert.Equal(0L, await ScalarAsync($"SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '{TextJobKeyPattern}'"));
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT text_state FROM bill_gates"));
    }

    [Fact]
    public async Task StoredTextAddsOneVersionRowWithHonestCountsAndMarksTheBillAsRead()
    {
        var textJob = await StartTextJobAsync();

        await AdmitTextAsync(textJob, charsRead: 59_993, charsTotal: 143_221);

        Assert.Equal("Read", await ScalarAsync("SELECT text_state FROM bill_gates"));
        Assert.Equal(("BILLS-119hr7ih", "Introduced in House", new DateOnly(2025, 3, 1), BillTextFixtures.CongressUrl(), 59_993, 143_221),
            await TextRowAsync());
        Assert.Equal("BILLS-119hr7ih", await ScalarAsync("SELECT text_package_id FROM bill_gates"));
    }

    [Fact]
    public async Task AVersionWithNoDateStoresNoDate()
    {
        var textJob = await StartTextJobAsync();

        await store.AdmitTextAsync(textJob, TextEvidence(IntroducedText with { Date = null }, await ExtractionAsync(textJob), 5, 5), default);

        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT version_date FROM bill_text_versions"));
    }

    [Fact]
    public async Task StoringTheSameTextTwiceAddsNoSecondRow()
    {
        var textJob = await StartTextJobAsync();
        await AdmitTextAsync(textJob, 10, 10);

        await AdmitTextAsync(textJob, 10, 10);

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM bill_text_versions"));
    }

    [Fact]
    public async Task ALaterUpdateOfABillWhoseVersionIsStoredReadsItWithoutAnotherDownload()
    {
        await AdmitTextAsync(await StartTextJobAsync(), 10, 10);
        var later = await EnqueueDetailAsync("later");
        var result = await AdmitDetailWithTextAsync(later, DetailBill(UpdatedAt.AddDays(3), cosponsors: 0), passed: true);
        var versions = result.Jobs.Single(job => job.Url == BillTextFixtures.VersionsUrl).JobId;

        var admitted = await store.AdmitTextVersionsAsync(BillsWithText(), versions, IntroducedText, default);

        Assert.Empty(admitted.Jobs);
        Assert.Equal("Read", await ScalarAsync($"SELECT text_state FROM bill_gates WHERE detail_job_id = '{later}'"));
        Assert.Equal(1L, await ScalarAsync($"SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '{TextJobKeyPattern}'"));
    }

    [Fact]
    public async Task ADifferentVersionOfTheSameBillGetsItsOwnRow()
    {
        await AdmitTextAsync(await StartTextJobAsync(), 10, 10);
        var reported = IntroducedText with { PackageId = "BILLS-119hr7rh", Type = "Reported in House" };
        var textJob = await StartTextJobAsync(reported, name: "second");

        await store.AdmitTextAsync(textJob, TextEvidence(reported, await ExtractionAsync(textJob), 4, 4), default);

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM bill_text_versions"));
    }

    private const string CheckViolation = "23514";
    private const string ForeignKeyViolation = "23503";

    [Fact]
    public async Task AnUnchangedCopyOfARowWithANewPackageIsAcceptedSoEachRefusalBelowComesFromItsOwnColumn()
    {
        await AdmitTextAsync(await StartTextJobAsync(), 10, 10);

        await InsertVariantAsync("version_type", "'Reported in House'");

        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM bill_text_versions"));
    }

    [Theory]
    [InlineData("chars_total", "30")]
    [InlineData("chars_read", "60001")]
    [InlineData("chars_read", "0")]
    [InlineData("version_type", "' '")]
    [InlineData("source_url", "'https://example.com/119/bills/hr7/BILLS-119hr7rh.htm'")]
    [InlineData("package_id", "'BILLS-119hr8ih'")]
    [InlineData("package_id", "'BILLS-118hr7ih'")]
    [InlineData("package_id", "'PLAW-118publ21'")]
    public async Task TheDatabaseRefusesATextRowWhoseCountsUrlOrPackageContradictItsBill(string column, string valueSql)
    {
        await AdmitTextAsync(await StartTextJobAsync(), 10, 10);

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => InsertVariantAsync(column, valueSql));

        Assert.Equal(CheckViolation, refusal.SqlState);
    }

    [Fact]
    public async Task AReadStateNeedsItsPackageAndAPackageNeedsTheReadStateSoThePackageIsNeverGuessedFromNewest()
    {
        await AdmitTextAsync(await StartTextJobAsync(), 10, 10);
        var (second, _) = await StartVersionsAsync("second");

        var noPackage = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connectionString, $"UPDATE bill_gates SET text_state = 'Read' WHERE detail_job_id = '{second}'"));
        var noState = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connectionString, $"UPDATE bill_gates SET text_package_id = 'BILLS-119hr7ih' WHERE detail_job_id = '{second}'"));
        var noText = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connectionString, $"UPDATE bill_gates SET text_state = 'NoText', text_package_id = 'BILLS-119hr7ih' WHERE detail_job_id = '{second}'"));

        Assert.Equal([CheckViolation, CheckViolation, CheckViolation], [noPackage.SqlState, noState.SqlState, noText.SqlState]);
    }

    [Fact]
    public async Task AGateCannotPointAtAPackageThatHasNoRowSoReadAlwaysMeansTheTextIsStored()
    {
        var (detail, _) = await StartVersionsAsync();

        var refusal = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString,
            $"UPDATE bill_gates SET text_state = 'Read', text_package_id = 'BILLS-119hr7zz' WHERE detail_job_id = '{detail}'"));

        Assert.Equal(ForeignKeyViolation, refusal.SqlState);
    }

    [Fact]
    public async Task ABillWithACongressGovSummaryCannotHaveATextStateBecauseNoTextJobExistsForIt()
    {
        var detail = await EnqueueDetailAsync("detail");
        await AdmitDetailWithTextAsync(detail, DetailBill(UpdatedAt, cosponsors: 0, summaries: 2), passed: true);

        var refusal = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connectionString, $"UPDATE bill_gates SET text_state = 'NoText' WHERE detail_job_id = '{detail}'"));

        Assert.Equal(CheckViolation, refusal.SqlState);
    }

    [Fact]
    public async Task ATextExtractionOfAnotherJobIsRefusedSoARowNeverCitesEvidenceThatTheJobDidNotCapture()
    {
        var (_, versions) = await StartVersionsAsync();
        var result = await store.AdmitTextVersionsAsync(BillsWithText(), versions, IntroducedText, default);
        var textJob = Assert.Single(result.Jobs).JobId;
        var foreignExtraction = await ExtractionAsync(versions);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.AdmitTextAsync(textJob, TextEvidence(IntroducedText, foreignExtraction, 10, 10), default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_text_versions"));
        Assert.Equal(DBNull.Value, await ScalarAsync("SELECT text_state FROM bill_gates"));
    }

    [Fact]
    public async Task TheDatabaseRefusesATextStateOtherThanNoTextOrRead()
    {
        _ = await StartVersionsAsync();

        await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connectionString, "UPDATE bill_gates SET text_state = 'Maybe'"));
    }

    [Fact]
    public async Task AVersionRowNeedsItsExtraction()
    {
        var textJob = await StartTextJobAsync();
        await AdmitTextAsync(textJob, 10, 10);

        await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(connectionString, $"UPDATE bill_text_versions SET extraction_id = '{new string('0', 64)}'"));
    }

    private Task InsertVariantAsync(string column, string valueSql)
    {
        string[] columns = ["congress", "bill_type", "bill_number", "package_id", "version_type", "version_date", "source_url",
            "extraction_id", "chars_total", "chars_read"];
        var select = string.Join(", ", columns.Select(name =>
            name == column ? $"{valueSql} AS {name}" : name == "package_id" ? "'BILLS-119hr7rh' AS package_id" : name));
        return ExecuteAsync(connectionString,
            $"INSERT INTO bill_text_versions ({string.Join(", ", columns)}) SELECT {select} FROM bill_text_versions");
    }

    private static ConfiguredCollectionSource BillsWithText(bool enabled = true) =>
        new(BillTextFixtures.Configuration(enabled), SourceId, Backfill.AddDays(400));

    private async Task<DiscoveryAdmissionResult> AdmitDetailWithTextAsync(string detailJobId, CongressBill bill, bool passed) =>
        await store.AdmitDetailAsync(BillsWithText(), detailJobId, bill,
            passed ? BillGateOutcome.Passed(BillGateReason.CoveredSponsor) : BillGateOutcome.NotRelevant(BillGateReason.UntrackedPolicyArea),
            BillGateFixtures.Revision().Id, await ExtractionAsync(detailJobId), default);

    private async Task<(string Detail, string Versions)> StartVersionsAsync(string name = "detail")
    {
        var detail = await EnqueueDetailAsync(name);
        var result = await AdmitDetailWithTextAsync(detail, DetailBill(UpdatedAt.AddDays(detailSequence), cosponsors: 0), passed: true);
        return (detail, result.Jobs.Single(job => job.Url == BillTextFixtures.VersionsUrl).JobId);
    }

    private async Task<string> StartTextJobAsync(CongressBillTextVersion? version = null, string name = "detail")
    {
        var (_, versions) = await StartVersionsAsync(name);
        var result = await store.AdmitTextVersionsAsync(BillsWithText(), versions, version ?? IntroducedText, default);
        return Assert.Single(result.Jobs).JobId;
    }

    private async Task AdmitTextAsync(string textJob, int charsRead, int charsTotal) =>
        await store.AdmitTextAsync(textJob, TextEvidence(IntroducedText, await ExtractionAsync(textJob), charsRead, charsTotal), default);

    private static CongressBillTextEvidence TextEvidence(CongressBillTextVersion version, string extractionId, int charsRead, int charsTotal) =>
        new(version, new CongressBillTextReading("Bill: 119 hr 7", charsRead, charsTotal), extractionId);

    private async Task<(string Package, string Type, DateOnly Date, string Url, int CharsRead, int CharsTotal)> TextRowAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT package_id, version_type, version_date, source_url, chars_read, chars_total FROM bill_text_versions", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateOnly>(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5));
    }
}
