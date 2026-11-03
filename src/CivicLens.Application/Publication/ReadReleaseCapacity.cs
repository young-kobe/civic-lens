using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public sealed class ReadReleaseCapacity(IPublicationStore publications, IReleaseDirectory releases)
{
    public async Task<ReleaseCapacity> ExecuteAsync(CancellationToken cancellationToken)
    {
        var waiting = await publications.CountApprovedOutsideActiveReleaseAsync([], cancellationToken);
        if (waiting == 0) return new(0, 0);
        var state = await publications.GetStateAsync(cancellationToken);
        if (state.Active is not { } active) return new(0, Math.Max(0, waiting - PublicationProtocol.MaximumRecordsPerRelease));
        var manifest = await PreviousRelease.LoadManifestAsync(releases, active, cancellationToken);
        var held = manifest.Records.Length + manifest.Withdrawals.Length;
        if (manifest.Withdrawals.Length > 0)
            waiting = await publications.CountApprovedOutsideActiveReleaseAsync(
                [.. manifest.Withdrawals.Select(withdrawal => new PublishedRecordReference(withdrawal.Kind, withdrawal.RecordId))], cancellationToken);
        return new(held, Math.Max(0, held + waiting - PublicationProtocol.MaximumRecordsPerRelease));
    }
}
