using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// The bill line that a list row, a card, or a vote page shows for each record of one release. An action shows the line of its own entry.
/// A vote shows the line of the first action on its bill that the release holds, which is also the action that its page links.
/// </summary>
public sealed class ReleaseBillLines
{
    public static ReleaseBillLines None { get; } = new(new Dictionary<string, VoteBillLink>());

    private readonly IReadOnlyDictionary<string, VoteBillLink> votes;

    private ReleaseBillLines(IReadOnlyDictionary<string, VoteBillLink> votes) => this.votes = votes;

    public static ReleaseBillLines From(IEnumerable<PublishedRecordEntry> releaseRecords,
        IReadOnlyDictionary<string, ImmutableArray<string>> voteBillActions)
    {
        var actions = releaseRecords.Where(entry => entry.Kind == PublishedRecordKind.Action).ToDictionary(entry => entry.RecordId, StringComparer.Ordinal);
        var votes = new Dictionary<string, VoteBillLink>(StringComparer.Ordinal);
        foreach (var (voteRecordId, actionRecordIds) in voteBillActions)
            if (actionRecordIds.FirstOrDefault(actions.ContainsKey) is { } actionRecordId)
                votes[voteRecordId] = new(actionRecordId, actions[actionRecordId].BillLine);
        return new(votes);
    }

    public string? Of(PublishedRecordEntry entry) => entry.Kind == PublishedRecordKind.Action ? entry.BillLine : BillOfVote(entry.RecordId)?.Line;

    public bool AnyOf(IEnumerable<PublishedRecordEntry> entries) => entries.Any(entry => Of(entry) is not null);

    public VoteBillLink? BillOfVote(string voteRecordId) => votes.GetValueOrDefault(voteRecordId);

    public IEnumerable<ReleaseVoteBill> VoteBills() =>
        votes.OrderBy(vote => vote.Key, StringComparer.Ordinal).Select(vote => new ReleaseVoteBill(vote.Key, vote.Value.ActionRecordId));
}
