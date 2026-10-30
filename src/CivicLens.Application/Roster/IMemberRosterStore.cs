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
}
