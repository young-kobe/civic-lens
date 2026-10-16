using System.Collections.Immutable;
using System.Globalization;

namespace CivicLens.Application.Briefs;

public sealed record BriefSelection(ImmutableArray<BriefSelectedRecord> Sent, int Considered)
{
    public static BriefSelection Select(IReadOnlyCollection<BriefRecord> records,
        Func<ImmutableArray<BriefSelectedRecord>, bool> fits)
    {
        var sent = ImmutableArray<BriefSelectedRecord>.Empty;
        foreach (var group in OrderedGroups(records))
        {
            var candidate = sent.AddRange(group.Records.Select((record, index) => Selected(record, sent.Length + index)));
            if (!fits(candidate)) break;
            sent = candidate;
        }
        return new(sent, records.Count);
    }

    private static BriefSelectedRecord Selected(BriefRecord record, int index) =>
        new("R" + (index + 1).ToString(CultureInfo.InvariantCulture), record);

    private static IEnumerable<Group> OrderedGroups(IEnumerable<BriefRecord> records) =>
        records.GroupBy(KeyOf, StringComparer.Ordinal)
            .Select(group => new Group(group.Key, [.. group.OrderBy(record => record.RecordId, StringComparer.Ordinal)]))
            .OrderByDescending(group => group.Records.Length)
            .ThenByDescending(group => group.Records.SelectMany(record => record.OfficialIds).Distinct(StringComparer.Ordinal).Count())
            .ThenByDescending(group => group.Records.Sum(record => record.SponsorshipEntries))
            .ThenBy(group => group.Key, StringComparer.Ordinal);

    private static string KeyOf(BriefRecord record) =>
        record.Bill is { } bill ? $"bill|{bill}" : $"{record.Kind}|{record.RecordId}";

    private sealed record Group(string Key, ImmutableArray<BriefRecord> Records);
}
