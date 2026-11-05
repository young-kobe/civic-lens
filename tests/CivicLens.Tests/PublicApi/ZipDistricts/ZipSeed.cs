using System.Globalization;
using CivicLens.Application.Districts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Districts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.ZipDistricts;

/// <summary>One official with one term as the release name rows hold it.</summary>
internal sealed record SeededSeat(string Id, string Name, string Kind, string State, int? District, string Start = "2025-01-03", string? End = null, string? Party = "Democratic");

/// <summary>The district snapshot and the release names that the ZIP route reads.</summary>
internal static class ZipSeed
{
    /// <summary>The ZCTAs are 36009 (two districts), 99501 (at large), 20001 (delegate), 94505, and 12345 (two districts with the same land).</summary>
    public static readonly string[] Rows =
    [
        DistrictFixtures.Row("0101", "36009", 4938464),
        DistrictFixtures.Row("0102", "36009", 412306983),
        DistrictFixtures.Row("0200", "99501", 17227675),
        DistrictFixtures.Row("1198", "20001", 5302364),
        DistrictFixtures.Row("0614", "94505", 1679865),
        DistrictFixtures.Row("0102", "12345", 100),
        DistrictFixtures.Row("0101", "12345", 100)
    ];

    public static async Task AdmitDistrictsAsync(string database)
    {
        var attempts = PostgresCollectionAttemptStore.FromConnectionString(database);
        var attempt = await RosterFixtures.ImportCaptureAsync(attempts, new string('c', 64), new DateTimeOffset(2026, 11, 3, 6, 0, 0, TimeSpan.Zero), DistrictFixtures.SourceId);
        var rows = ZctaDistrictParser.Parse(DistrictFixtures.Bytes("﻿" + ZctaDistrictParser.Header + "\n" + string.Join('\n', Rows) + "\n"));
        await PostgresZctaDistrictStore.FromConnectionString(database).AdmitAsync(attempt, rows, default);
    }

    /// <summary>Writes the names of a release. Call <see cref="ViewsSeed.ActivateReleaseAsync"/> first, so the release row exists.</summary>
    public static async Task AddNamesAsync(string database, int release, params SeededSeat[] seats)
    {
        var number = release.ToString(CultureInfo.InvariantCulture);
        foreach (var seat in seats)
            await ViewsSeed.ExecuteAsync(database, $"""
                INSERT INTO release_officials (release_number, official_id, name) VALUES ({number}, '{seat.Id}', '{seat.Name}');
                INSERT INTO release_official_terms (release_number, official_id, "position", office, kind, state, district, party, start_on, end_on)
                VALUES ({number}, '{seat.Id}', 0, '{seat.Kind}', '{seat.Kind}', '{seat.State}', {seat.District?.ToString(CultureInfo.InvariantCulture) ?? "NULL"},
                        {(seat.Party is null ? "NULL" : $"'{seat.Party}'")}, '{seat.Start}', {(seat.End is null ? "NULL" : $"'{seat.End}'")});
                """);
    }

    /// <summary>Gives the active release one record, because the views read the release from its records.</summary>
    public static Task AddRecordAsync(string database, int release) => ViewsSeed.ExecuteAsync(database, $"""
        SET session_replication_role = replica;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
        VALUES ('vote', '{new string('a', 64)}', {release.ToString(CultureInfo.InvariantCulture)}, 1, NULL);
        """);
}
