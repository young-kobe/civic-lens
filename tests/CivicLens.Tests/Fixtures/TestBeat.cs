using CivicLens.Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.Fixtures;

internal static class TestBeat
{
    public static WorkerBeat Create(IWorkerHeartbeatStore? store = null, TimeSpan? interval = null, TimeProvider? clock = null,
        Microsoft.Extensions.Logging.ILogger<WorkerBeat>? logger = null) =>
        new(store ?? new RecordingHeartbeatStore(), "test", logger ?? NullLogger<WorkerBeat>.Instance, clock ?? TimeProvider.System, interval);
}
