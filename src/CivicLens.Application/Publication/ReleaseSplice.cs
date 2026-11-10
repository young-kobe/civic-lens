using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Builds a manifest from the tables and takes from the file only what the tables leave out: the names of officials, the five optional entry fields
/// that the tables do not hold, the time of the build, and the withdrawals of a release that no withdrawal row covers. The bill line and statement approver come from the
/// table. A file with no bill line keeps none, because a release from before the line has none for any revision.
/// </summary>
internal static class ReleaseSplice
{
    public static PublicationRelease Rebuild(PublicationRelease file, StoredRelease stored)
    {
        var listed = file.Records.ToDictionary(entry => new PublishedRecordReference(entry.Kind, entry.RecordId));
        var entries = stored.Records.Select(row => EntryOf(row, listed[new(row.Kind, row.RecordId)]));
        return new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = file.ReleaseNumber,
            PublishedAtUtc = file.PublishedAtUtc,
            Records = [.. ReleaseRecordOrder.NewestFirst(entries, ReleaseRecordOrder.Day, entry => entry.FirstPublishedAtUtc, entry => entry.RecordId)],
            Withdrawals = [.. ReleaseRecordOrder.WithdrawalsNewestFirst(HoldsWithdrawalsOnlyInFile(file, stored) ? file.Withdrawals : stored.Withdrawals)]
        };
    }

    public static bool HoldsWithdrawalsOnlyInFile(PublicationRelease file, StoredRelease stored) =>
        file.Withdrawals.Length > 0 && stored.Withdrawals.Count == 0 && stored.PredatesWithdrawalRows;

    public static byte[] Serialize(PublicationRelease release) => JsonSerializer.SerializeToUtf8Bytes(release, PublicationProtocol.JsonOptions);

    public static PublishedRecordReference? FirstDifferentRecord(PublicationRelease file, PublicationRelease rebuilt)
    {
        var inFile = file.Records.ToDictionary(entry => entry.RecordId, SerializeEntry);
        return rebuilt.Records.Where(entry => !inFile.TryGetValue(entry.RecordId, out var json) || json != SerializeEntry(entry))
            .Select(entry => new PublishedRecordReference(entry.Kind, entry.RecordId)).FirstOrDefault();
    }

    internal static PublishedRecordEntry EntryOf(StoredReleaseRecord row, PublishedRecordEntry fromFile)
    {
        var facts = row.Facts!;
        var names = fromFile.Officials.ToDictionary(official => official.Id, official => official.Name);
        return new PublishedRecordEntry
        {
            Kind = row.Kind,
            RecordId = row.RecordId,
            RevisionNumber = row.RevisionNumber,
            Headline = facts.Headline,
            FirstPublishedAtUtc = row.FirstPublishedAtUtc!.Value,
            Officials = [.. facts.OfficialIds.Select(id => new PublishedOfficial { Id = id, Name = names.GetValueOrDefault(id, string.Empty) })],
            IssueIds = facts.IssueIds,
            PolicyAreaIds = facts.PolicyAreaIds,
            SourceHost = facts.SourceHost,
            ApprovedBy = fromFile.ApprovedBy is null ? null : facts.ApprovedBy,
            EventDate = facts.EventDate,
            BillTitle = facts.BillTitle,
            LinkType = facts.LinkType,
            StatementRecordId = facts.StatementRecordId,
            TargetKind = facts.TargetKind,
            TargetRecordId = facts.TargetRecordId,
            MatchedOfficialId = facts.MatchedOfficialId,
            DocumentType = facts.DocumentType,
            YeaCount = fromFile.YeaCount,
            NayCount = fromFile.NayCount,
            Alignment = fromFile.Alignment,
            BillLine = fromFile.BillLine is null ? null : facts.BillLine,
            Chamber = fromFile.Chamber,
            VoteKind = fromFile.VoteKind,
            Congress = fromFile.Congress,
            CosponsorIds = fromFile.CosponsorIds
        };
    }

    private static string SerializeEntry(PublishedRecordEntry entry) => JsonSerializer.Serialize(entry, PublicationProtocol.JsonOptions);
}
