using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The vote rows that a publish adds for each official and Congress, and the vote records that it replaces or withdraws.</summary>
internal sealed class OfficialVoteChanges(Dictionary<string, Dictionary<int, List<PublishedOfficialVote>>> added, HashSet<string> replacedRecordIds)
{
    private static readonly IReadOnlyDictionary<int, List<PublishedOfficialVote>> None = new Dictionary<int, List<PublishedOfficialVote>>();

    public bool Touches(string officialId) => added.ContainsKey(officialId);

    public IReadOnlyDictionary<int, List<PublishedOfficialVote>> Of(string officialId) =>
        added.TryGetValue(officialId, out var byCongress) ? byCongress : None;

    public bool Replaces(string recordId) => replacedRecordIds.Contains(recordId);
}
