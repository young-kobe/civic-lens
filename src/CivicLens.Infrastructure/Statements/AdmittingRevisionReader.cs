using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Jobs.Persistence;
using CivicLens.Infrastructure.Statements.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicLens.Infrastructure.Statements;

internal sealed class AdmittingRevisionReader
{
    private CollectionConfigurationRevision? lastRevision;

    public async Task<CollectionConfigurationRevision?> ReadAsync(CollectionAttemptDbContext db, string candidateId,
        CancellationToken cancellationToken)
    {
        var definitionJson = await (from candidate in db.Set<StatementCandidateRow>().AsNoTracking()
                                    join jobAttempt in db.Set<JobAttemptRow>().AsNoTracking() on candidate.AttemptId equals jobAttempt.AttemptId
                                    join job in db.Set<JobRow>().AsNoTracking() on jobAttempt.JobId equals job.JobId
                                    where candidate.CandidateId == candidateId
                                    select job.DefinitionJson).SingleOrDefaultAsync(cancellationToken);
        return definitionJson is null ? null : ReadRevision(definitionJson);
    }

    private CollectionConfigurationRevision? ReadRevision(string definitionJson)
    {
        var memo = lastRevision;
        if (memo is not null && memo.Id == ReadRevisionId(definitionJson)) return memo;
        var revision = PostgresCollectionJobStore.ReadDefinition(definitionJson).ConfigurationRevision;
        lastRevision = revision ?? memo;
        return revision;
    }

    private static string? ReadRevisionId(string definitionJson)
    {
        try
        {
            using var document = JsonDocument.Parse(definitionJson);
            return document.RootElement.TryGetProperty("configurationRevision", out var revision) &&
                revision.ValueKind == JsonValueKind.Object && revision.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.String ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
