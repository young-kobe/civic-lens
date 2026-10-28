using CivicLens.Application.Roster;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Roster;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Roster;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresMemberPhotoStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string JobLockKey = "civic-lens-collection-jobs";

    private static readonly DateTimeOffset Morning = new(2026, 10, 28, 6, 0, 0, TimeSpan.Zero);
    private string connectionString = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresMemberRosterStore roster = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        roster = PostgresMemberRosterStore.FromConnectionString(connectionString);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ARowHoldsThePersonTheSourceUrlTheCopiedCreditAndTheAttemptOfItsCapture()
    {
        var attemptId = await ImportAsync('a', 0);

        await roster.AdmitPhotoAsync(Photo(attemptId), default);

        var row = Assert.Single(await ReadRowsAsync());
        Assert.Equal((attemptId, "a-person", MemberPhotoFixtures.PhotoUrl(), MemberPhoto.MemberGuideCredit, new string('a', 64)),
            (row.AttemptId, row.PersonId, row.SourceUrl, row.Credit, row.CaptureSha256));
    }

    [Fact]
    public async Task AChangedBodyAddsARowAndLeavesTheOldRowAndItsCapture()
    {
        var first = await ImportAsync('a', 0);
        var changed = await ImportAsync('b', 40);
        await roster.AdmitPhotoAsync(Photo(first), default);

        await roster.AdmitPhotoAsync(Photo(changed), default);

        Assert.Equal([(first, new string('a', 64)), (changed, new string('b', 64))],
            (await ReadRowsAsync()).Select(row => (row.AttemptId, row.CaptureSha256)));
    }

    [Fact]
    public async Task AnUnchangedBodyAddsNoRowSoAMonthlyRecheckLeavesNoDuplicate()
    {
        var first = await ImportAsync('a', 0);
        var recheck = await ImportAsync('a', 30);
        await roster.AdmitPhotoAsync(Photo(first), default);

        await roster.AdmitPhotoAsync(Photo(recheck), default);

        Assert.Equal([first], (await ReadRowsAsync()).Select(row => row.AttemptId));
    }

    [Fact]
    public async Task AnEarlierBodyThatReturnsAddsARowSoTheNewestRowIsTheLivePhoto()
    {
        var first = await ImportAsync('a', 0);
        var changed = await ImportAsync('b', 30);
        var returned = await ImportAsync('a', 60);
        await roster.AdmitPhotoAsync(Photo(first), default);
        await roster.AdmitPhotoAsync(Photo(changed), default);

        await roster.AdmitPhotoAsync(Photo(returned), default);

        Assert.Equal([first, changed, returned], (await ReadRowsAsync()).Select(row => row.AttemptId));
    }

    [Fact]
    public async Task ThePreparationOfOneAttemptRunTwiceWritesOneRow()
    {
        var first = await ImportAsync('a', 0);
        var changed = await ImportAsync('b', 30);
        await roster.AdmitPhotoAsync(Photo(first), default);
        await roster.AdmitPhotoAsync(Photo(changed), default);

        await roster.AdmitPhotoAsync(Photo(first), default);

        Assert.Equal([first, changed], (await ReadRowsAsync()).Select(row => row.AttemptId));
    }

    [Fact]
    public async Task TwoConcurrentPreparationsOfTheSameUnchangedBodyForOnePersonAddOneRowBecauseTheJobLockSerializesThem()
    {
        var first = await ImportAsync('a', 0);
        var second = await ImportAsync('a', 1);

        Task writes;
        await using (var holder = await HeldAdvisoryLock.AcquireAsync(connectionString, JobLockKey))
        {
            writes = Task.WhenAll(roster.AdmitPhotoAsync(Photo(first), default), roster.AdmitPhotoAsync(Photo(second), default));
            await HeldAdvisoryLock.WaitForWaiterAsync(connectionString, JobLockKey);
            Assert.False(writes.IsCompleted);
        }
        await writes;

        Assert.Single(await ReadRowsAsync());
    }

    [Fact]
    public async Task TwoPeopleWithTheSameImageBytesEachGetTheirOwnRow()
    {
        var first = await ImportAsync('a', 0);
        var second = await MemberPhotoFixtures.ImportCaptureAsync(attempts, new string('a', 64), Morning, "B000002");

        await roster.AdmitPhotoAsync(Photo(first), default);
        await roster.AdmitPhotoAsync(Photo(second, "another-person", "B000002"), default);

        Assert.Equal(["a-person", "another-person"], (await ReadRowsAsync()).Select(row => row.PersonId).Order());
    }

    [Fact]
    public async Task TheNewestPhotoOfAPersonIsTheRowOfItsLatestAttemptEvenWhenAnEarlierBodyReturned()
    {
        var first = await ImportAsync('a', 0);
        var changed = await ImportAsync('b', 30);
        var returned = await ImportAsync('a', 60);
        foreach (var attemptId in new[] { first, changed, returned }) await roster.AdmitPhotoAsync(Photo(attemptId), default);

        var photo = Assert.Single(await roster.ReadNewestPhotosAsync(["a-person"], default));

        Assert.Equal(new OfficialPhoto("a-person", MemberPhotoFixtures.PhotoUrl(), MemberPhoto.MemberGuideCredit, returned, new string('a', 64)), photo);
    }

    [Fact]
    public async Task OnlyTheNamedPeopleAreReadSoAnotherPersonOrAPersonWithNoRowAddsNothing()
    {
        await roster.AdmitPhotoAsync(Photo(await ImportAsync('a', 0)), default);
        await roster.AdmitPhotoAsync(Photo(await MemberPhotoFixtures.ImportCaptureAsync(attempts, new string('b', 64), Morning, "B000002"),
            "another-person", "B000002"), default);

        var photos = await roster.ReadNewestPhotosAsync(["another-person", "no-photo-person"], default);

        Assert.Equal(["another-person"], photos.Select(photo => photo.PersonId));
        Assert.Empty(await roster.ReadNewestPhotosAsync([], default));
    }

    private static MemberPhoto Photo(string attemptId, string personId = "a-person", string bioguideId = MemberPhotoFixtures.Bioguide) =>
        new(attemptId, personId, MemberPhotoFixtures.PhotoUrl(bioguideId), MemberPhoto.MemberGuideCredit);

    private Task<string> ImportAsync(char body, int day) =>
        MemberPhotoFixtures.ImportCaptureAsync(attempts, new string(body, 64), Morning.AddDays(day));

    private async Task<List<PhotoRow>> ReadRowsAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT photo.attempt_id, photo.person_id, photo.source_url, photo.credit, attempt.capture_sha256
            FROM official_photos photo JOIN collection_attempts attempt ON attempt.attempt_id = photo.attempt_id
            ORDER BY attempt.observed_at_utc_ticks, photo.attempt_id
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<PhotoRow>();
        while (await reader.ReadAsync())
            rows.Add(new PhotoRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        return rows;
    }

    private sealed record PhotoRow(string AttemptId, string PersonId, string SourceUrl, string Credit, string CaptureSha256);
}
