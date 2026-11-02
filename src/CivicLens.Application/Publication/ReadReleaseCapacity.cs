using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>The records and withdrawals of the active release, and the approved records that the cap leaves out of it.</summary>
public sealed record ReleaseCapacity(int Held, int WaitingForRoom);

public sealed class ReadReleaseCapacity(IPublicationStore publications, IReleaseDirectory releases)
{
    public async Task<ReleaseCapacity> ExecuteAsync(CancellationToken cancellationToken)
    {
        var state = await publications.GetStateAsync(cancellationToken);
        var held = 0;
        if (state.Active is { } active)
        {
            var manifest = await PreviousRelease.LoadManifestAsync(releases, active, cancellationToken);
            held = manifest.Records.Length + manifest.Withdrawals.Length;
        }
        var waiting = await publications.CountApprovedOutsideActiveReleaseAsync(cancellationToken);
        return new(held, Math.Max(0, held + waiting - PublicationProtocol.MaximumRecordsPerRelease));
    }
}
