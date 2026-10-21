using System.Collections.Immutable;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Owns who may withdraw or return a record, and the manifest entries that a withdrawal writes.</summary>
internal static class PublicationWithdrawals
{
    public static Dictionary<PublishedRecordReference, string> ValidateNotes(ImmutableArray<RecordNote> withdrawals, bool byCode)
    {
        if (byCode && !withdrawals.IsEmpty) throw new ArgumentException("The code subject withdraws no records.", nameof(withdrawals));
        return withdrawals.ToDictionary(withdrawal => withdrawal.Reference, withdrawal => withdrawal.ValidText(nameof(withdrawals)));
    }

    public static void RequireNoReturnByCode(PreviousRelease previous, ImmutableArray<PublishedRecordReference> records, bool byCode)
    {
        if (!byCode) return;
        foreach (var record in records.Where(record => previous.IsWithdrawn(record.RecordId)))
            throw new WithdrawnFactException(record);
    }

    public static List<PublishedWithdrawal> Create(PreviousRelease previous, PublicationCorrections corrections, DateTimeOffset now,
        IReadOnlyDictionary<PublishedRecordReference, string> notes)
    {
        var created = new List<PublishedWithdrawal>(notes.Count);
        foreach (var (reference, note) in notes)
        {
            var entry = previous.Find(reference.Kind, reference.RecordId)
                ?? throw new ArgumentException($"{reference} is not in the active release, so it cannot be withdrawn.");
            created.Add(new()
            {
                Kind = reference.Kind,
                RecordId = reference.RecordId,
                WithdrawnRevisionNumber = entry.RevisionNumber,
                FirstPublishedAtUtc = entry.FirstPublishedAtUtc,
                WithdrawnAtUtc = now,
                Note = note,
                Corrections = corrections.Earlier(reference)
            });
        }
        return created;
    }
}
