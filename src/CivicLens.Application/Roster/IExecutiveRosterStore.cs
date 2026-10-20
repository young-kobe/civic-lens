namespace CivicLens.Application.Roster;

public interface IExecutiveRosterStore
{
    Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken);

    Task AdmitAsync(string attemptId, IReadOnlyList<ExecutiveOfficial> officials, CancellationToken cancellationToken);
}
