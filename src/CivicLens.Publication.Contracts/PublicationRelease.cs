namespace CivicLens.Publication.Contracts;

public sealed record PublicationRelease
{
    public required int SchemaVersion { get; init; }
    public required int ReleaseNumber { get; init; }
    public required DateTimeOffset PublishedAtUtc { get; init; }
    public required PublishedRecordEntry[] Records { get; init; }
    public required PublishedWithdrawal[] Withdrawals { get; init; }

    public void Validate()
    {
        if (SchemaVersion != PublicationProtocol.SchemaVersion ||
            ReleaseNumber < 1 || Records is null || Withdrawals is null ||
            Records.Length + Withdrawals.Length > PublicationProtocol.MaximumRecordsPerRelease)
            throw new InvalidDataException("Release manifest is invalid.");
        foreach (var entry in Records) entry?.Validate();
        foreach (var withdrawal in Withdrawals) withdrawal?.Validate();
        if (Records.Any(entry => entry is null) || Withdrawals.Any(withdrawal => withdrawal is null) ||
            Records.Select(entry => entry.RecordId).Concat(Withdrawals.Select(withdrawal => withdrawal.RecordId))
                .Distinct(StringComparer.Ordinal).Count() != Records.Length + Withdrawals.Length)
            throw new InvalidDataException("Release manifest records must be present, and each record ID must be unique across all kinds and withdrawals.");
    }
}
