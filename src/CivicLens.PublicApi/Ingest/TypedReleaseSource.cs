using CivicLens.PublicApi.Records;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// Reads the active release and its index documents from the <c>public_api</c> views.
/// Each read opens one <see cref="RecordSnapshot"/>, so a release that publication activates during the ingest cannot mix into it.
/// </summary>
public sealed class TypedReleaseSource(RecordSnapshot snapshot) : IReleaseSource
{
    public async Task<IReleaseRead?> ReadChangedAsync(CancellationToken cancellationToken) => await TypedReleaseRead.OpenAsync(snapshot, cancellationToken);

    public void Stored()
    {
    }
}
