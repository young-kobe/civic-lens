namespace CivicLens.Application.Districts;

public interface IZctaDistrictStore
{
    Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken);

    Task AdmitAsync(string attemptId, IReadOnlyList<ZctaDistrict> rows, CancellationToken cancellationToken);
}
