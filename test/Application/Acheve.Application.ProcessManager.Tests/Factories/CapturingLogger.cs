using Microsoft.Extensions.Logging;

namespace Acheve.Application.ProcessManager.Tests.Factories
{
    // Remembers the scopes the code under test opened.
    public sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<object?> Scopes { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            Scopes.Add(state);
            return new NoopScope();
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NoopScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
