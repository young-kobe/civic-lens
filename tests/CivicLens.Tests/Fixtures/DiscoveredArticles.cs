using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;

namespace CivicLens.Tests.Fixtures;

internal static class DiscoveredArticles
{
    public static async Task<DiscoveryAdmissionResult> AdmitAsync(PostgresCollectionJobStore jobs,
        PostgresCollectionAttemptStore attempts, ConfiguredCollectionSource source, string key, params string[] urls)
    {
        var attemptId = "discovery-" + key;
        var request = source.CreateJobDefinition(DateOnly.FromDateTime(DateTime.UtcNow))
            .CreateRequest(attemptId, Path.GetFullPath(".runtime/test-captures"));
        var receipt = CollectionFixtures.Receipt(request) with
        {
            Discovery = new DiscoveryResult { Status = DiscoveryStatus.Parsed, Urls = urls, OutOfScopeCount = 0 }
        };
        await CollectionAttemptImporter.ImportAsync(attemptId, request, receipt, attempts, default);
        return await jobs.AdmitAsync(source, attemptId, key, default);
    }
}
