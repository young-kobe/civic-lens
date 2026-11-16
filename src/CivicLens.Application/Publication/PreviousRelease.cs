using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class PreviousRelease
{
    private PreviousRelease(string? directoryName, IReadOnlyList<PublishedRecordEntry> entries,
        IReadOnlyList<PublishedWithdrawal> withdrawals, IReadOnlyDictionary<string, PublishedOfficialProfile> officials)
    {
        DirectoryName = directoryName;
        Entries = entries;
        Withdrawals = withdrawals;
        Officials = officials;
    }

    public string? DirectoryName { get; }

    public IReadOnlyList<PublishedRecordEntry> Entries { get; }

    public IReadOnlyList<PublishedWithdrawal> Withdrawals { get; }

    public IReadOnlyDictionary<string, PublishedOfficialProfile> Officials { get; }

    public static async Task<PreviousRelease> LoadAsync(IReleaseDirectory releases, PublicationReleaseSummary? active,
        CancellationToken cancellationToken)
    {
        if (active is null) return new(null, [], [], new Dictionary<string, PublishedOfficialProfile>());
        var manifest = await LoadManifestAsync(releases, active, cancellationToken);
        return new(active.DirectoryName, manifest.Records, manifest.Withdrawals, await ReadOfficialsAsync(releases, active.DirectoryName, cancellationToken));
    }

    public static async Task<PublicationRelease> LoadManifestAsync(IReleaseDirectory releases, PublicationReleaseSummary active,
        CancellationToken cancellationToken)
    {
        var manifest = await LoadManifestAsync(releases, active.DirectoryName, cancellationToken);
        if (manifest.ReleaseNumber != active.ReleaseNumber)
            throw new InvalidDataException("Release manifest does not match the active release.");
        return manifest;
    }

    public static async Task<PublicationRelease> LoadManifestAsync(IReleaseDirectory releases, string directoryName,
        CancellationToken cancellationToken)
    {
        var bytes = await releases.ReadFileAsync(directoryName, PublicationProtocol.ManifestPath,
            PublicationProtocol.MaximumManifestBytes, cancellationToken);
        return ParseManifest(bytes);
    }

    public static PublicationRelease ParseManifest(byte[] bytes)
    {
        var manifest = ReadManifest(bytes);
        manifest.Validate();
        return manifest;
    }

    private static PublicationRelease ReadManifest(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<PublicationRelease>(bytes, PublicationProtocol.JsonOptions)
                ?? throw new InvalidDataException("Release manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Release manifest does not match the readable schema.", exception);
        }
    }

    internal static async Task<Dictionary<string, PublishedOfficialProfile>> ReadOfficialsAsync(IReleaseDirectory releases,
        string directoryName, CancellationToken cancellationToken)
    {
        var bytes = await releases.ReadFileAsync(directoryName, PublicationProtocol.OfficialsPath, PublishedOfficials.MaximumBytes,
            cancellationToken);
        var officials = JsonSerializer.Deserialize<PublishedOfficials>(bytes, PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("Release officials file is empty.");
        officials.Validate();
        return officials.Officials.ToDictionary(official => official.Id, StringComparer.Ordinal);
    }

    public bool IsWithdrawn(string recordId) => FindWithdrawal(recordId) is not null;

    public PublishedWithdrawal? FindWithdrawal(string recordId) =>
        Withdrawals.FirstOrDefault(withdrawal => withdrawal.RecordId == recordId);

    public PublishedRecordEntry? Find(PublishedRecordKind kind, string recordId) =>
        Entries.FirstOrDefault(entry => entry.Kind == kind && entry.RecordId == recordId);
}
