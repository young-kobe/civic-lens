using CivicLens.Application.Collection;

namespace CivicLens.Application.Roster;

public interface IMemberRosterStore
{
    Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken);

    /// <summary>Writes the snapshot and, when a photo source is given, its photo jobs in one transaction.</summary>
    Task AdmitAsync(string attemptId, IReadOnlyList<RosterMember> members, ConfiguredCollectionSource? photos,
        CancellationToken cancellationToken);

    Task AdmitPhotoJobsAsync(ConfiguredCollectionSource photos, CancellationToken cancellationToken);

    Task AdmitPhotoAsync(MemberPhoto photo, CancellationToken cancellationToken);

    /// <summary>Reads the newest photo of each named person who has one. The newest photo is the one of the latest captured attempt.</summary>
    Task<IReadOnlyList<OfficialPhoto>> ReadNewestPhotosAsync(IReadOnlyCollection<string> personIds, CancellationToken cancellationToken);

    Task<IReadOnlyList<RosterMember>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken);

    /// <summary>Reads the attempt of the snapshot that <see cref="ReadLatestAsync"/> reads. Null means that the source has no snapshot.</summary>
    Task<string?> ReadLatestSnapshotAttemptIdAsync(string sourceId, CancellationToken cancellationToken);

    /// <summary>Fills the last name of each snapshot row that holds none, from the parse of the same capture. A filled name never changes. Returns the rows filled.</summary>
    Task<int> FillLastNamesAsync(string attemptId, IReadOnlyList<RosterMember> parsed, CancellationToken cancellationToken);
}
