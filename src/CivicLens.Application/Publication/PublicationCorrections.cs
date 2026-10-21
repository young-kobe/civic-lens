using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Owns the correction list of each record that a publish writes, and the rule for its notes.</summary>
internal sealed class PublicationCorrections
{
    private readonly PreviousRelease previous;
    private readonly DateTimeOffset now;
    private readonly bool byCode;
    private readonly IReadOnlyDictionary<PublishedRecordReference, string> notes;
    private readonly IReadOnlyDictionary<PublishedRecordReference, PublishedCorrection[]> earlier;

    private PublicationCorrections(PreviousRelease previous, DateTimeOffset now, bool byCode,
        IReadOnlyDictionary<PublishedRecordReference, string> notes,
        IReadOnlyDictionary<PublishedRecordReference, PublishedCorrection[]> earlier)
    {
        this.previous = previous;
        this.now = now;
        this.byCode = byCode;
        this.notes = notes;
        this.earlier = earlier;
    }

    public static Dictionary<PublishedRecordReference, string> ValidateNotes(ImmutableArray<RecordNote> notes,
        ImmutableArray<PublishedRecordReference> records, bool byCode)
    {
        if (notes.IsDefault) throw new ArgumentException("Notes must not be default.", nameof(notes));
        if (byCode && !notes.IsEmpty) throw new ArgumentException("The code subject gives no correction notes.", nameof(notes));
        var requested = records.ToHashSet();
        var trimmed = new Dictionary<PublishedRecordReference, string>();
        foreach (var note in notes)
        {
            var text = note.ValidText(nameof(notes));
            if (!requested.Contains(note.Reference))
                throw new ArgumentException($"{note.Reference} has a note but is not in the release.", nameof(notes));
            if (!trimmed.TryAdd(note.Reference, text))
                throw new ArgumentException($"{note.Reference} has more than one note.", nameof(notes));
        }
        return trimmed;
    }

    public static async Task<PublicationCorrections> LoadAsync(IReleaseDirectory releases, PreviousRelease previous, DateTimeOffset now,
        bool byCode, IReadOnlyDictionary<PublishedRecordReference, string> notes, ImmutableArray<PublishedRecordReference> records,
        CancellationToken cancellationToken)
    {
        var earlier = new Dictionary<PublishedRecordReference, PublishedCorrection[]>();
        foreach (var record in records.Where(record => previous.Find(record.Kind, record.RecordId) is not null))
            earlier[record] = await ReadAsync(releases, previous.DirectoryName!, record, cancellationToken);
        return new(previous, now, byCode, notes, earlier);
    }

    public PublishedCorrection[] For(PublishedRecordKind kind, string recordId)
    {
        var reference = new PublishedRecordReference(kind, recordId);
        if (previous.IsWithdrawn(recordId)) throw new WithdrawnRecordException(reference);
        var note = notes.GetValueOrDefault(reference);
        if (previous.Find(kind, recordId) is not { } replaced)
            return note is null ? [] : throw new ArgumentException($"{reference} is not in the active release, so it takes no note.");
        if (note is null && !byCode)
            throw new MissingCorrectionNoteException(reference);
        var kept = earlier[reference];
        if (note is not null && PublishedCorrection.NotedCount(kept) >= PublishedCorrection.MaximumNotedEntries)
            throw new CorrectionLimitException(reference);
        return PublishedCorrection.Prepend(kept, new() { ReplacedRevisionNumber = replaced.RevisionNumber, PublishedAtUtc = now, Note = note });
    }

    public PublishedCorrection[] Earlier(PublishedRecordReference record) => earlier[record];

    private static async Task<PublishedCorrection[]> ReadAsync(IReleaseDirectory releases, string directoryName,
        PublishedRecordReference record, CancellationToken cancellationToken)
    {
        var json = await releases.ReadFileAsync(directoryName, PublicationProtocol.RecordDataPath(record.RecordId),
            PublishedRecordFiles.MaximumCitedRecordBytes, cancellationToken);
        try
        {
            return (JsonSerializer.Deserialize<CorrectionsOnly>(json, PublishedRecordFiles.PartialReadOptions)
                ?? throw new InvalidDataException("A carried record file is empty.")).Corrections;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("A carried record has no correction list.", exception);
        }
    }

    private sealed record CorrectionsOnly(PublishedCorrection[] Corrections);
}
