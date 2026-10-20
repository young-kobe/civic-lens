using Microsoft.Extensions.Hosting;

namespace CivicLens.PublicApi.Schema;

/// <summary>Applies the schema before the service accepts requests.</summary>
public sealed class SchemaStartup(SchemaMigrator migrator) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => migrator.ApplyAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
