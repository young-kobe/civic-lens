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
    private readonly IReadOnlyDictionary<string, PublishedAction> earlierActions;

    private PublicationCorrections(PreviousRelease previous, DateTimeOffset now,
        IReadOnlyDictionary<PublishedRecordReference, PublishedCorrection[]> earlier, IReadOnlyDictionary<string, PublishedVote> earlierVotes,
        IReadOnlyDictionary<string, PublishedAction> earlierActions)
    {
        this.previous = previous;
        this.now = now;
        this.earlier = earlier;
        this.earlierVotes = earlierVotes;
        this.earlierActions = earlierActions;
    }

    public static async Task<PublicationCorrections> LoadAsync(IPublicationStore publications, IReleaseDirectory releases,
        int? previousReleaseNumber, PreviousRelease previous, DateTimeOffset now,
        ImmutableArray<PublishedRecordReference> records, CancellationToken cancellationToken)
    {
        var earlier = new Dictionary<PublishedRecordReference, PublishedCorrection[]>();
        var earlierVotes = new Dictionary<string, PublishedVote>();
        var earlierActions = new Dictionary<string, PublishedAction>();
        var published = records.Select(record => previous.Find(record.Kind, record.RecordId)).OfType<PublishedRecordEntry>()
            .Select(PublishedRevisionBinding.Of).Distinct().ToArray();
        var stored = previousReleaseNumber is { } number
            ? await publications.ReadCorrectionHistoriesAsync(number, published, cancellationToken)
            : new Dictionary<PublishedRecordReference, StoredCorrectionHistory>();
        foreach (var record in records)
        {
            if (previous.FindWithdrawal(record.RecordId) is { } withdrawal) earlier[record] = withdrawal.Corrections;
            else if (previous.Find(record.Kind, record.RecordId) is { } expected)
            {
                if (stored.TryGetValue(record, out var history)) earlier[record] = history.Corrections;
                if (history is not null && !HasTextRewrite(record.Kind)) continue;
                var json = await ReadJsonAsync(releases, previous.DirectoryName!, record, cancellationToken);
                PublishedCorrection[] corrections;
                if (record.Kind == PublishedRecordKind.Vote)
                {
                    var vote = RecordIn<PublishedVote>(json);
                    vote.Validate();
                    RequireRevision(new(PublishedRecordKind.Vote, vote.RecordId, vote.RevisionNumber), expected);
                    earlierVotes[record.RecordId] = vote;
                    corrections = vote.Corrections;
                }
                else if (record.Kind == PublishedRecordKind.Action)
                {
                    var action = RecordIn<PublishedAction>(json);
                    action.Validate();
                    RequireRevision(new(PublishedRecordKind.Action, action.RecordId, action.RevisionNumber), expected);
                    earlierActions[record.RecordId] = action;
                    corrections = action.Corrections;
                }
                else
                {
                    var content = RecordFileEntry.ReadContent(record.Kind, json);
                    RequireRevision(PublishedRevisionBinding.Of(content.Entry), expected);
                    corrections = content.Corrections;
                }
                if (history is null) earlier[record] = corrections;
            }
        }
        return new(previous, now, earlier, earlierVotes, earlierActions);
    }

    // The earlier record file is the only source of the replaced vote or bill action: review keeps no history of published revisions.
    public PublishedVote? EarlierVote(string recordId) => earlierVotes.GetValueOrDefault(recordId);

    public PublishedAction? EarlierAction(string recordId) => earlierActions.GetValueOrDefault(recordId);

    // A vote and a bill action can name a text rewrite, so their loaders compare the new record with the replaced one.
    private static bool HasTextRewrite(PublishedRecordKind kind) => kind is PublishedRecordKind.Vote or PublishedRecordKind.Action;

    private static void RequireRevision(PublishedRevisionBinding read, PublishedRecordEntry expected)
    {
        if (read != PublishedRevisionBinding.Of(expected))
            throw new InvalidDataException("A previous record file does not match its published revision.");
    }

    // A code approval needs no note. A person approval that replaces a published record needs the note that its approval holds.
    // A note on a first publication is dropped.
    public PublishedCorrection[] For(PublishedRecordKind kind, string recordId, int revisionNumber, ImmutableArray<ReviewDecision> decisions,
        PublishedTextRewrite textRewrite = PublishedTextRewrite.None)
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
            TextRewrite = note is null ? textRewrite : PublishedTextRewrite.None
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

    private static TRecord RecordIn<TRecord>(byte[] json) where TRecord : class
    {
        try
        {
            return JsonSerializer.Deserialize<TRecord>(json, PublicationProtocol.JsonOptions)
                ?? throw new InvalidDataException("A carried record file is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("A carried record cannot be read.", exception);
        }
    }

    private sealed record CorrectionsOnly(PublishedCorrection[] Corrections);
}
