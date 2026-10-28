using CivicLens.Application.Roster;
using CivicLens.Application.Votes;
using CivicLens.Infrastructure.Analysis;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Roster.Persistence;
using CivicLens.Infrastructure.Votes;
using CivicLens.Infrastructure.Votes.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CivicLens.Infrastructure.Roster;

/// <summary>Retains one append-only snapshot of the member roster for each capture body not yet seen.</summary>
public sealed class PostgresMemberRosterStore(IDbContextFactory<CollectionAttemptDbContext> contextFactory) : IMemberRosterStore
{
    private readonly PostgresAnalysisQueue<VoteAnalysisRow, VoteAnalysisBudgetRow, VoteDraftRun> voteQueue = VoteDraftStaleness.NewQueue(contextFactory);

    public static PostgresMemberRosterStore FromConnectionString(string connectionString)
    {
        _ = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        return new PostgresMemberRosterStore(new PooledDbContextFactory<CollectionAttemptDbContext>(options));
    }

    public async Task<bool> HasSnapshotOfCaptureAsync(string attemptId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await RosterSnapshotRules.HasSnapshotOfCaptureAsync(db, attemptId, cancellationToken);
    }

    public async Task AdmitAsync(string attemptId, IReadOnlyList<RosterMember> members, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentNullException.ThrowIfNull(members);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        RosterSnapshotRules.AddHeader(db, attemptId);
        foreach (var member in members)
        {
            db.Add(ToRow(attemptId, member));
            db.AddRange(member.Terms.Select(term => ToRow(attemptId, member.BioguideId, term)));
        }
        _ = await VoteDraftStaleness.RequeueSenateUnmatchedAsync(voteQueue, db, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AdmitPhotoAsync(MemberPhoto photo, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(photo);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var captureSha256 = (await db.Attempts.AsNoTracking().SingleOrDefaultAsync(attempt => attempt.AttemptId == photo.AttemptId, cancellationToken))
            ?.CaptureSha256 ?? throw new InvalidOperationException("A member photo needs an imported captured attempt.");
        var newestCaptureSha256 = await (from row in db.Set<OfficialPhotoRow>().AsNoTracking()
                                         where row.PersonId == photo.PersonId
                                         join attempt in db.Attempts.AsNoTracking() on row.AttemptId equals attempt.AttemptId
                                         orderby attempt.ObservedAtUtcTicks descending, attempt.AttemptId descending
                                         select attempt.CaptureSha256).FirstOrDefaultAsync(cancellationToken);
        if (newestCaptureSha256 == captureSha256 ||
            await db.Set<OfficialPhotoRow>().AnyAsync(row => row.AttemptId == photo.AttemptId, cancellationToken))
            return;
        db.Add(new OfficialPhotoRow { AttemptId = photo.AttemptId, PersonId = photo.PersonId, SourceUrl = photo.SourceUrl, Credit = photo.Credit });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RosterMember>> ReadLatestAsync(string sourceId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await RosterSnapshotRules.ReadLatestSnapshotAttemptIdAsync(db, sourceId, cancellationToken) is not { } snapshotAttemptId)
            return [];
        var members = await db.Set<RosterMemberRow>().AsNoTracking().Where(row => row.AttemptId == snapshotAttemptId)
            .OrderBy(row => row.BioguideId).ToListAsync(cancellationToken);
        var terms = (await db.Set<RosterTermRow>().AsNoTracking().Where(row => row.AttemptId == snapshotAttemptId)
            .OrderBy(row => row.TermIndex).ToListAsync(cancellationToken)).ToLookup(row => row.BioguideId);
        return [.. members.Select(member => ToMember(member, terms[member.BioguideId]))];
    }

    public async Task<IReadOnlyList<SenatorLisId>> ReadSenatorLisIdsAsync(string sourceId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var snapshotIds = RosterSnapshotRules.SnapshotAttempts(db, sourceId).Select(attempt => attempt.AttemptId);
        var rows = await db.Set<RosterMemberRow>().AsNoTracking()
            .Where(member => snapshotIds.Contains(member.AttemptId) && db.Set<RosterTermRow>().Any(term =>
                term.AttemptId == member.AttemptId && term.BioguideId == member.BioguideId && term.Chamber == MemberRosterParser.Senate))
            .Select(member => new { member.BioguideId, member.LisId }).Distinct()
            .OrderBy(member => member.BioguideId).ThenBy(member => member.LisId).ToListAsync(cancellationToken);
        return [.. rows.Select(row => new SenatorLisId(row.BioguideId, row.LisId))];
    }

    private static RosterMemberRow ToRow(string attemptId, RosterMember member) => new()
    {
        AttemptId = attemptId,
        BioguideId = member.BioguideId,
        LisId = member.LisId,
        FullName = member.FullName,
        Office = member.Office,
        Phone = member.Phone,
        Url = member.Url
    };

    private static RosterTermRow ToRow(string attemptId, string bioguideId, RosterTerm term) => new()
    {
        AttemptId = attemptId,
        BioguideId = bioguideId,
        TermIndex = term.Index,
        Chamber = term.Chamber,
        StartsOn = term.Start,
        EndsOn = term.End,
        State = term.State,
        District = term.District,
        SenateClass = term.SenateClass,
        Party = term.Party,
        Caucus = term.Caucus
    };

    private static RosterMember ToMember(RosterMemberRow member, IEnumerable<RosterTermRow> terms) =>
        new(member.BioguideId, member.LisId, member.FullName, member.Office, member.Phone, member.Url,
            [.. terms.Select(term => new RosterTerm(term.TermIndex, term.Chamber, term.StartsOn, term.EndsOn, term.State,
                term.District, term.SenateClass, term.Party, term.Caucus))]);
}
