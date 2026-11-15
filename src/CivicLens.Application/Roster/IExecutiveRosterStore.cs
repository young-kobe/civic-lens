namespace CivicLens.Application.Roster;

public interface IExecutiveRosterStore
{
    Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken);

    Task AdmitAsync(string attemptId, IReadOnlyList<ExecutiveOfficial> officials, CancellationToken cancellationToken);

    Task<IReadOnlyList<ExecutiveOfficial>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken);

    /// <summary>Reads the attempt of the snapshot that <see cref="ReadLatestAsync"/> reads. Null means that the source has no snapshot.</summary>
    Task<string?> ReadLatestSnapshotAttemptIdAsync(string sourceId, CancellationToken cancellationToken);

    /// <summary>Fills the last name of each snapshot row that holds none, from the parse of the same capture. A filled name never changes. Returns the rows filled.</summary>
    Task<int> FillLastNamesAsync(string attemptId, IReadOnlyList<ExecutiveOfficial> parsed, CancellationToken cancellationToken);
}
