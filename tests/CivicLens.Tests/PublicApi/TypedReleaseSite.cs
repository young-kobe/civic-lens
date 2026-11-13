using CivicLens.Application.Publication;
using CivicLens.Tests.PublicApi.Ingest;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi;

/// <summary>
/// Publishes releases of sample records into the operational tables that the <c>public_api</c> views read, as publication does.
/// Each listed revision has a range in <c>publication_record_states</c>. A new release closes the ranges of the revisions that it does not list
/// at the release before it, so an earlier release that becomes active again still lists what it held.
/// One implicit transaction writes the ranges and activates the release, so a running ingest never sees half of a release.
/// </summary>
internal sealed class TypedReleaseSite(string database)
{
    private readonly HashSet<(string RecordId, int Revision)> seeded = [];
    private readonly Dictionary<(string Kind, string RecordId, int Revision), int> open = [];
    private int? latest;

    /// <summary>Publishes a new release that lists exactly the given records. Its number must be above every published number.</summary>
    public async Task PublishAsync(int number, params SampleRecord[] records)
    {
        if (number <= latest) throw new InvalidOperationException("A new release must be newer than every published release. ActivateAsync makes an earlier one active.");
        await TypedSearchSeed.WriteAsync(database, [.. records.Where(record => seeded.Add((record.Entry.RecordId, record.Entry.RevisionNumber)))]);

        var listed = records.Select(record => (PublishedRecordReference.NameOf(record.Entry.Kind), record.Entry.RecordId, record.Entry.RevisionNumber)).ToHashSet();
        var sql = new List<string> { "SET session_replication_role = replica;", ViewsSeed.ActivateReleaseSql(number, PublishedAt(number), records.Length) };
        foreach (var range in open.Keys.Where(range => !listed.Contains(range)).ToArray())
        {
            sql.Add($"UPDATE publication_record_states SET last_release = {latest} WHERE kind = '{range.Kind}' AND record_id = '{range.RecordId}' AND first_release = {open[range]};");
            open.Remove(range);
        }
        foreach (var range in listed.Where(range => !open.ContainsKey(range)))
        {
            sql.Add($"INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES ('{range.Item1}', '{range.Item2}', {number}, {range.Item3}, NULL);");
            open[range] = number;
        }
        sql.AddRange(records.SelectMany(record => record.Entry.Officials).DistinctBy(official => official.Id).Select(official =>
            $"INSERT INTO release_officials (release_number, official_id, name) VALUES ({number}, {TypedSearchSeed.Q(official.Id)}, {TypedSearchSeed.Q(official.Name)});"));
        await ViewsSeed.ExecuteAsync(database, string.Join('\n', sql));
        latest = number;
    }

    /// <summary>Makes a release active without a change to the ranges. A published number becomes active as a rollback does. An unpublished earlier number lists only the ranges that cover it.</summary>
    public Task ActivateAsync(int number) => ViewsSeed.ActivateReleaseAsync(database, number, PublishedAt(number));

    /// <summary>The publication time of a release, one day apart for each number.</summary>
    private static DateTimeOffset PublishedAt(int number) => new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero).AddDays(number);
}
