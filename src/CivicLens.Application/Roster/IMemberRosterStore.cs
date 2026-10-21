namespace CivicLens.Application.Roster;

public interface IMemberRosterStore
{
    Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken);

    Task AdmitAsync(string attemptId, IReadOnlyList<RosterMember> members, CancellationToken cancellationToken);

    Task<IReadOnlyList<RosterMember>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken);

    Task<IReadOnlyList<SenatorLisId>> ReadSenatorLisIdsAsync(string sourceId, CancellationToken cancellationToken);
}
