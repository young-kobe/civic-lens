using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Tests.Fixtures;

public static class PublishedEntryFixtures
{
    public static async Task SeedEntriesAsync(string connectionString, IReadOnlyCollection<PublishedRecordEntry> entries,
        bool allowMissingRevisionParents = false)
    {
        ArgumentNullException.ThrowIfNull(entries);
        foreach (var entry in entries) entry.Validate();
        if (entries.GroupBy(entry => (entry.Kind, entry.RecordId))
            .Any(group => group.Select(entry => entry.FirstPublishedAtUtc).Distinct().Skip(1).Any()))
            throw new InvalidDataException("Entries for one record must have the same first publication time.");

        await using var db = new CollectionAttemptDbContext(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        await db.Database.OpenConnectionAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (allowMissingRevisionParents)
            await db.Database.ExecuteSqlRawAsync("SET LOCAL session_replication_role = replica");

        var firstPublications = entries.Select(PublishedFirstPublication.Of).DistinctBy(record => (record.Kind, record.RecordId)).ToArray();
        foreach (var batch in firstPublications.Chunk(256))
            await InternalMethods.InvokeAsync("CivicLens.Infrastructure.Publication.PublishedRecords", "InsertAsync", db, batch, CancellationToken.None);
        foreach (var batch in firstPublications.Chunk(256))
            await InternalMethods.InvokeAsync("CivicLens.Infrastructure.Publication.PublishedRecords", "RequireStoredTimesAsync", db, batch, CancellationToken.None);

        foreach (var batch in entries.Chunk(256))
            await InternalMethods.InvokeAsync("CivicLens.Infrastructure.Publication.PublishedEntries", "InsertAsync", db, batch,
                "The fixture entry conflicts with a stored entry.", CancellationToken.None);

        await transaction.CommitAsync();
    }

    public static async Task<PublicationCommit> WithCorrectionHistoriesAsync(IPublicationStore publications, PublicationCommit commit)
    {
        if (commit.Records.IsDefault || commit.CorrectionHistories.IsDefault || !commit.CorrectionHistories.IsEmpty) return commit;
        var histories = await publications.PrepareCorrectionHistoriesAsync(
            [.. commit.Records.Select(record => new PublicationCorrectionHistory(record, []))], CancellationToken.None);
        return commit with { CorrectionHistories = [.. histories] };
    }

    public static PublishedRecordEntry Action(string recordId, DateTimeOffset firstPublishedAt, int revision = 1) => new()
    {
        Kind = PublishedRecordKind.Action,
        RecordId = recordId,
        RevisionNumber = revision,
        Headline = "A bill about housing",
        FirstPublishedAtUtc = firstPublishedAt,
        Officials = [new() { Id = "S000002", Name = "Sam Second" }, new() { Id = "S000001", Name = "Sam First" }],
        IssueIds = ["tax", "housing"],
        PolicyAreaIds = ["economics"],
        SourceHost = "www.congress.gov",
        EventDate = new DateOnly(2026, 3, 1),
        BillTitle = "To fund housing"
    };

    public static PublishedRecordEntry DocumentChange(string recordId, int revision, string headline, DateTimeOffset firstPublishedAt) => new()
    {
        Kind = PublishedRecordKind.DocumentChange,
        RecordId = recordId,
        RevisionNumber = revision,
        Headline = headline,
        FirstPublishedAtUtc = firstPublishedAt,
        Officials = [],
        IssueIds = [],
        PolicyAreaIds = []
    };

    public static PublishedRecordEntry Relationship(string recordId, string statementId, string targetId, DateTimeOffset firstPublishedAt) => new()
    {
        Kind = PublishedRecordKind.Relationship,
        RecordId = recordId,
        RevisionNumber = 1,
        Headline = "A statement about housing",
        FirstPublishedAtUtc = firstPublishedAt,
        Officials = [new() { Id = "S000001", Name = "Sam First" }, new() { Id = "S000002", Name = "Sam Second" }, new() { Id = "S000003", Name = "Sam Third" }],
        IssueIds = ["tax", "housing"],
        PolicyAreaIds = ["economics"],
        EventDate = new DateOnly(2026, 3, 1),
        LinkType = PublishedRelationshipType.DescribesOwnBill,
        StatementRecordId = statementId,
        TargetKind = PublishedRecordKind.Action,
        TargetRecordId = targetId,
        MatchedOfficialId = "S000002"
    };

    public static PublishedRecordEntry Vote(string recordId, DateTimeOffset firstPublishedAt) => new()
    {
        Kind = PublishedRecordKind.Vote,
        RecordId = recordId,
        RevisionNumber = 1,
        Headline = "A vote on housing",
        FirstPublishedAtUtc = firstPublishedAt,
        Officials = [new() { Id = "S000001", Name = "Sam First" }],
        IssueIds = ["housing"],
        PolicyAreaIds = [],
        EventDate = new DateOnly(2026, 3, 1),
        YeaCount = 2,
        NayCount = 1,
        Alignment = PublishedVoteAlignment.PartiesAgree
    };

    public static PublishedRecordEntry ExecutiveAction(string recordId, DateTimeOffset firstPublishedAt) => new()
    {
        Kind = PublishedRecordKind.ExecutiveAction,
        RecordId = recordId,
        RevisionNumber = 1,
        Headline = "An order about housing",
        FirstPublishedAtUtc = firstPublishedAt,
        Officials = [],
        IssueIds = ["housing"],
        PolicyAreaIds = [],
        EventDate = new DateOnly(2026, 3, 1),
        DocumentType = PublishedExecutiveActionKind.ExecutiveOrder
    };

    public static string InsertActionRevisionSql(string recordId, int revision = 1) => $$"""
        SET session_replication_role = replica;
        INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
        VALUES ('{{recordId}}', {{revision}}, 'owner', 'Person', '{}', 0)
        """;
}
