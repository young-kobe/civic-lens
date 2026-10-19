namespace CivicLens.Application.Roster;

public interface IMemberRosterStore
{
    Task AdmitAsync(string attemptId, IReadOnlyList<RosterMember> members, CancellationToken cancellationToken);

    Task<IReadOnlyList<RosterMember>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken);
}
