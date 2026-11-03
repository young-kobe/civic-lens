using System.Collections.Concurrent;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

internal sealed class PublishingNotifications : IAsyncDisposable
{
    private readonly NpgsqlConnection listener;
    private readonly ConcurrentQueue<string> payloads = new();

    private PublishingNotifications(NpgsqlConnection listener)
    {
        this.listener = listener;
        listener.Notification += (_, eventArgs) => payloads.Enqueue(eventArgs.Payload);
    }

    public static async Task<PublishingNotifications> ListenAsync(string connectionString)
    {
        var listener = new NpgsqlConnection(connectionString);
        await listener.OpenAsync();
        await using var command = new NpgsqlCommand("LISTEN civic_lens_publishing", listener);
        await command.ExecuteNonQueryAsync();
        return new(listener);
    }

    public async Task<List<string>> DrainAsync()
    {
        while (await listener.WaitAsync(500)) { }
        var drained = new List<string>();
        while (payloads.TryDequeue(out var payload)) drained.Add(payload);
        return drained;
    }

    public async ValueTask DisposeAsync() => await listener.DisposeAsync();
}
