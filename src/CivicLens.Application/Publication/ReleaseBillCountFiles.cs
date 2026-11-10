using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

/// <summary>
/// Owns the proof that the files of one release agree with the tables that feed its bill counts. The membership of actions and votes must match the
/// manifest and the state table in both directions. Each record file must validate and agree with its stored projection. The officials file must list the official rows.
/// </summary>
internal static class ReleaseBillCountFiles
{
    public static async Task<IReadOnlyList<PublishedRevisionBinding>> ReadValidatedMembersAsync(IPublicationStore publications,
        IReleaseDirectory releases, PublicationReleaseSummary release, CancellationToken cancellationToken)
    {
        var source = await publications.ReadBillCountSourceAsync(release.ReleaseNumber, cancellationToken);
        var manifest = await PreviousRelease.LoadManifestAsync(releases, release, cancellationToken);
        RequireSameMembers(manifest, source);
        var actions = source.Actions.ToDictionary(action => (action.RecordId, action.RevisionNumber));
        var votes = source.Votes.ToDictionary(vote => (vote.RecordId, vote.RevisionNumber));
        foreach (var member in source.Members)
        {
            var json = await ReleaseRecordFiles.ReadAsync(releases, release.DirectoryName, member.RecordId, cancellationToken);
            if (member.Kind == PublishedRecordKind.Action) RequireAction(member, json, actions);
            else RequireVote(member, json, votes);
        }
        var officials = await PreviousRelease.ReadOfficialsAsync(releases, release.DirectoryName, cancellationToken);
        if (!SameIds(officials.Keys, source.OfficialIds))
            throw new InvalidDataException("The officials file differs from the stored release officials.");
        return source.Members;
    }

    private static void RequireSameMembers(PublicationRelease manifest, ReleaseBillCountSource source)
    {
        var listed = manifest.Records.Where(entry => entry.Kind is PublishedRecordKind.Action or PublishedRecordKind.Vote)
            .Select(PublishedRevisionBinding.Of).ToArray();
        var stored = source.Members.ToHashSet();
        if (listed.Length != stored.Count || !stored.SetEquals(listed))
            throw new InvalidDataException("The release manifest and the state table list different action and vote revisions.");
    }

    private static void RequireAction(PublishedRevisionBinding member, byte[] json,
        IReadOnlyDictionary<(string RecordId, int RevisionNumber), BillCountActionProjection> stored)
    {
        var file = ReleaseRecordFiles.Deserialize<PublishedAction>(json);
        file.Validate();
        RequireIdentity(member, file.RecordId, file.RevisionNumber);
        if (!stored.TryGetValue((member.RecordId, member.RevisionNumber), out var projection))
            throw new InvalidDataException("A selected action revision is not stored.");
        var entry = file.ToEntry();
        if (file.Congress != projection.Congress || entry.EventDate != projection.EventOn ||
            !SameIds(entry.Officials.Select(official => official.Id), projection.SponsorIds) ||
            !SameIds(entry.CosponsorIds ?? [], projection.CosponsorIds))
            throw new InvalidDataException("An action file differs from its stored bill count projection.");
    }

    private static void RequireVote(PublishedRevisionBinding member, byte[] json,
        IReadOnlyDictionary<(string RecordId, int RevisionNumber), BillCountVoteProjection> stored)
    {
        var file = ReleaseRecordFiles.Deserialize<PublishedVote>(json);
        file.Validate();
        RequireIdentity(member, file.RecordId, file.RevisionNumber);
        if (!stored.TryGetValue((member.RecordId, member.RevisionNumber), out var projection))
            throw new InvalidDataException("A selected vote revision is not stored.");
        if (file.Congress != projection.Congress)
            throw new InvalidDataException("A vote file differs from its stored Congress.");
    }

    private static void RequireIdentity(PublishedRevisionBinding member, string recordId, int revisionNumber)
    {
        if (recordId != member.RecordId || revisionNumber != member.RevisionNumber)
            throw new InvalidDataException("A record file differs from its stored release revision.");
    }

    private static bool SameIds(IEnumerable<string> first, IEnumerable<string> second) =>
        first.ToHashSet(StringComparer.Ordinal).SetEquals(second);
}
