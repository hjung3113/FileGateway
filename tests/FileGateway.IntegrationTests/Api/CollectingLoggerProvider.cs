using Microsoft.Extensions.Logging;

namespace FileGateway.IntegrationTests.Api;

public sealed record CollectedLogEntry(
    string Category,
    LogLevel Level,
    string Message,
    Exception? Exception = null,
    IReadOnlyDictionary<string, object?>? Properties = null);

public sealed class CollectingLoggerProvider : ILoggerProvider
{
    private readonly object _gate = new();
    public List<CollectedLogEntry> Entries { get; } = [];

    public IReadOnlyList<CollectedLogEntry> Snapshot()
    {
        lock (_gate) return Entries.ToArray();
    }

    public ILogger CreateLogger(string categoryName) => new CollectingLogger(this, categoryName);

    public void Dispose() { }

    private sealed class CollectingLogger(CollectingLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                ? values.ToDictionary(pair => pair.Key, pair => pair.Value)
                : null;
            lock (owner._gate)
                owner.Entries.Add(new CollectedLogEntry(
                    category, logLevel, formatter(state, exception), exception, properties));
        }
    }
}
