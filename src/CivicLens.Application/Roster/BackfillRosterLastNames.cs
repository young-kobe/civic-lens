using CivicLens.Application.Collection;
using CivicLens.Core.Collection;

namespace CivicLens.Application.Roster;

/// <summary>
/// Fills the last names of the latest member and executive roster snapshots, which imports wrote before the roster kept <c>name.last</c>.
/// It parses the saved capture of the snapshot with the parser of the import, so the names are the ones that the import reads today.
/// Only the latest snapshot of each source is filled, because publication reads no other. A filled name never changes, so a second run fills nothing.
/// </summary>
public sealed class BackfillRosterLastNames(CollectionConfiguration configuration, IMemberRosterStore members, IExecutiveRosterStore executives,
    ICollectionAttemptStore attempts, ICaptureBodyReader bodies, string artifactRoot)
{
    /// <summary>Returns the snapshot rows that this run filled.</summary>
    public async Task<int> ExecuteAsync(CancellationToken cancellationToken)
    {
        var filled = 0;
        if (configuration.SourceOf(ApiParser.CongressLegislators) is { } memberSource &&
            await members.ReadLatestSnapshotAttemptIdAsync(memberSource.Id, cancellationToken) is { } memberAttempt)
            filled += await members.FillLastNamesAsync(memberAttempt, MemberRosterParser.Parse(await ReadBodyAsync(memberAttempt, cancellationToken)),
                cancellationToken);
        if (configuration.SourceOf(ApiParser.CongressLegislatorsExecutive) is { } executiveSource &&
            await executives.ReadLatestSnapshotAttemptIdAsync(executiveSource.Id, cancellationToken) is { } executiveAttempt)
            filled += await executives.FillLastNamesAsync(executiveAttempt,
                ExecutiveRosterParser.Parse(await ReadBodyAsync(executiveAttempt, cancellationToken)), cancellationToken);
        return filled;
    }

    private async Task<byte[]> ReadBodyAsync(string attemptId, CancellationToken cancellationToken) =>
        await attempts.GetAsync(attemptId, cancellationToken) is { AttemptResult: CapturedAttemptResult captured }
            ? await bodies.ReadAsync(captured, artifactRoot, cancellationToken)
            : throw new InvalidDataException("A roster snapshot attempt has no captured response.");
}
