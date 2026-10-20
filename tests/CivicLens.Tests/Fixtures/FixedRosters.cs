using CivicLens.Application.Collection;
using CivicLens.Application.Publication;
using CivicLens.Application.Roster;

namespace CivicLens.Tests.Fixtures;

internal sealed class FixedMemberRoster(RosterMember[] members) : IMemberRosterStore
{
    public Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task AdmitAsync(string attemptId, IReadOnlyList<RosterMember> roster, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<RosterMember>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RosterMember>>(members);
}

internal sealed class FixedExecutiveRoster(ExecutiveOfficial[] officials) : IExecutiveRosterStore
{
    public Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken) => throw new NotSupportedException();

    public Task AdmitAsync(string attemptId, IReadOnlyList<ExecutiveOfficial> roster, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<ExecutiveOfficial>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ExecutiveOfficial>>(officials);
}

internal static class FixedRosters
{
    public static PublicationOfficials NoOfficials() => new(RosterFixtures.Configuration(), new FixedMemberRoster([]), new FixedExecutiveRoster([]));

    public static PublicationOfficials NamedOnly(IEnumerable<string> ids) => new(
        RosterFixtures.Configuration() with { People = [.. ids.Select(id => new PersonConfiguration { Id = id, Name = id })] },
        new FixedMemberRoster([]), new FixedExecutiveRoster([]));
}
