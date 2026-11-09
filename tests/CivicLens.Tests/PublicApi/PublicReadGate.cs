using Microsoft.Extensions.Logging;

namespace CivicLens.Tests.PublicApi;

/// <summary>Pauses after the first completed public API query so a test can change the active release mid-read.</summary>
internal sealed class FirstPublicApiReadGate(TimeSpan timeout) : ILoggerProvider
{
    private readonly TaskCompletionSource firstRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim resume = new(false);
    private int claimed;

    public Task FirstRead => firstRead.Task;
    public ILogger CreateLogger(string categoryName) => new GateLogger(this, categoryName);

    public void Resume() => resume.Set();
    public void Dispose() => resume.Dispose();

    private void Observe(string category, EventId eventId, IReadOnlyList<KeyValuePair<string, object?>> state)
    {
        if (category != "Npgsql.Command" || eventId.Name != "CommandExecutionCompleted") return;
        var commandText = state.FirstOrDefault(item => string.Equals(item.Key, "CommandText", StringComparison.OrdinalIgnoreCase)).Value?.ToString();
        if (commandText is null || !commandText.Contains("public_api.", StringComparison.OrdinalIgnoreCase)) return;
        if (Interlocked.CompareExchange(ref claimed, 1, 0) != 0) return;

        firstRead.TrySetResult();
        if (!resume.Wait(timeout)) throw new TimeoutException("Timed out waiting for the snapshot probe release switch.");
    }

    private sealed class GateLogger(FirstPublicApiReadGate owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Trace;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (state is IReadOnlyList<KeyValuePair<string, object?>> values) owner.Observe(category, eventId, values);
        }
    }
}
