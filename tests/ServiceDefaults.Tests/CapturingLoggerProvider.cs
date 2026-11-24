using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.ServiceDefaults.Tests;

/// <summary>
/// A logging provider that reads scopes as an exporter with <c>IncludeScopes</c> does, through the factory's shared scope
/// provider, and keeps each entry with every scope value it carried when it was written.
/// </summary>
public sealed class CapturingLoggerProvider : ILoggerProvider, ISupportExternalScope
{
    private readonly ConcurrentQueue<CapturedLog> logs = [];

    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public IReadOnlyList<CapturedLog> Logs => [.. logs];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => scopes = scopeProvider;

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(CapturingLoggerProvider provider) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => provider.scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            List<string> values = [];
            provider.scopes.ForEachScope(
                (scope, list) =>
                {
                    if (scope is IEnumerable<KeyValuePair<string, object?>> pairs)
                    {
                        list.AddRange(pairs.Select(pair => $"{pair.Key}={pair.Value}"));
                    }
                    else
                    {
                        list.Add(scope?.ToString() ?? string.Empty);
                    }
                },
                values);
            provider.logs.Enqueue(new CapturedLog(formatter(state, exception), values));
        }
    }
}
