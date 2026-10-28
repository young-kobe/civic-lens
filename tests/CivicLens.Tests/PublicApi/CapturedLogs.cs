using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace CivicLens.Tests.PublicApi;

/// <summary>Keeps every log line that the service writes, so a test can search the lines for text that must never be logged.</summary>
internal sealed class CapturedLogs : ILoggerProvider
{
    private readonly ConcurrentQueue<string> lines = new();

    public IReadOnlyCollection<string> Lines => [.. lines];

    public ILogger CreateLogger(string categoryName) => new Capture(categoryName, lines);

    public void Dispose()
    {
    }

    private sealed class Capture(string category, ConcurrentQueue<string> lines) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            lines.Enqueue($"{category} {formatter(state, exception)} {exception}");
    }
}
