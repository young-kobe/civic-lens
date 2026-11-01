using System.Collections.Immutable;
using System.Text.Json;
using CivicLens.Core.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>Owns the correction list of each record that a publish writes, and the rule for its notes.</summary>
internal sealed class PublicationCorrections
{
    private readonly PreviousRelease previous;
    private readonly DateTimeOffset now;
    private readonly IReadOnlyDictionary<PublishedRecordReference, PublishedCorrection[]> earlier;
    private readonly IReadOnlyDictionary<string, PublishedVote> earlierVotes;

    private PublicationCorrections(PreviousRelease previous, DateTimeOffset now,
        IReadOnlyDictionary<PublishedRecordReference, PublishedCorrection[]> earlier, IReadOnlyDictionary<string, PublishedVote> earlierVotes)
    {
        this.previous = previous;
        this.now = now;
        this.earlier = earlier;
        this.earlierVotes = earlierVotes;
    }

    public static async Task<PublicationCorrections> LoadAsync(IReleaseDirectory releases, PreviousRelease previous, DateTimeOffset now,
        ImmutableArray<PublishedRecordReference> records, CancellationToken cancellationToken)
    {
        var earlier = new Dictionary<PublishedRecordReference, PublishedCorrection[]>();
        var earlierVotes = new Dictionary<string, PublishedVote>();
        foreach (var record in records)
        {
            if (previous.FindWithdrawal(record.RecordId) is { } withdrawal) earlier[record] = withdrawal.Corrections;
            else if (previous.Find(record.Kind, record.RecordId) is not null)
            {
                var json = await ReadJsonAsync(releases, previous.DirectoryName!, record, cancellationToken);
                earlier[record] = CorrectionsIn(json);
                if (record.Kind == PublishedRecordKind.Vote) earlierVotes[record.RecordId] = VoteIn(json);
            }
        }
        return new(previous, now, earlier, earlierVotes);
    }

    // The earlier record file is the only source of the replaced vote: review keeps no history of published revisions.
    public PublishedVote? EarlierVote(string recordId) => earlierVotes.GetValueOrDefault(recordId);

    // A code approval needs no note. A person approval that replaces a published record needs the note that its approval holds.
    // A note on a first publication is dropped.
    public PublishedCorrection[] For(PublishedRecordKind kind, string recordId, int revisionNumber, ImmutableArray<ReviewDecision> decisions,
        bool textRewritten = false)
    {
        var reference = new PublishedRecordReference(kind, recordId);
        var replacedRevision = previous.Find(kind, recordId)?.RevisionNumber ?? previous.FindWithdrawal(recordId)?.WithdrawnRevisionNumber;
        if (replacedRevision is not { } replaced) return [];
        var note = PublishedRecordRules.CorrectionNoteOf(revisionNumber, decisions);
        if (note is null && !PublishedRecordRules.IsApprovedByCode(revisionNumber, decisions))
            throw new MissingCorrectionNoteException(reference);
        var kept = earlier[reference];
        if (note is not null && PublishedCorrection.IsAtNoteLimit(kept))
            throw new CorrectionLimitException(reference);
        return PublishedCorrection.Prepend(kept, new()
        {
            ReplacedRevisionNumber = replaced,
            PublishedAtUtc = now,
            Note = note,
            FactsUnchanged = note is null && textRewritten
        });
    }

    public PublishedCorrection[] Earlier(PublishedRecordReference record) => earlier[record];

    internal static async Task<PublishedCorrection[]> ReadAsync(IReleaseDirectory releases, string directoryName,
        PublishedRecordReference record, CancellationToken cancellationToken) =>
        CorrectionsIn(await ReadJsonAsync(releases, directoryName, record, cancellationToken));

    private static Task<byte[]> ReadJsonAsync(IReleaseDirectory releases, string directoryName, PublishedRecordReference record,
        CancellationToken cancellationToken) =>
        releases.ReadFileAsync(directoryName, PublicationProtocol.RecordDataPath(record.RecordId),
            PublishedRecordFiles.MaximumCitedRecordBytes, cancellationToken);

    private static PublishedCorrection[] CorrectionsIn(byte[] json)
    {
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

    private static PublishedVote VoteIn(byte[] json)
    {
        try
        {
            return JsonSerializer.Deserialize<PublishedVote>(json, PublicationProtocol.JsonOptions)
                ?? throw new InvalidDataException("A carried record file is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("A carried vote record cannot be read.", exception);
        }
    }

    private sealed record CorrectionsOnly(PublishedCorrection[] Corrections);
}
