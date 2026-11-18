using System.Diagnostics;
using Acheve.Common.Shared;
using Microsoft.Extensions.Logging;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public sealed class CustomerTelemetryTests : IDisposable
    {
        private readonly string _sourceName = $"customer-telemetry-tests-{Guid.NewGuid():N}";
        private readonly ActivitySource _source;
        private readonly ActivityListener _listener;
        private readonly ScopeRecordingLogger _logger = new();

        public CustomerTelemetryTests()
        {
            _source = new ActivitySource(_sourceName);
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == _sourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose()
        {
            _listener.Dispose();
            _source.Dispose();
        }

        [Fact]
        public void Tags_the_current_activity_and_opens_a_log_scope()
        {
            using var activity = _source.StartActivity("work");

            using var scope = CustomerTelemetry.BeginScope(_logger, "acme");

            scope.ShouldNotBeNull();
            activity!.GetTagItem(CustomerTelemetry.Tag).ShouldBe("acme");
            _logger.Scopes.ShouldHaveSingleItem();
            ((IEnumerable<KeyValuePair<string, object?>>)_logger.Scopes[0]!)
                .ShouldContain(new KeyValuePair<string, object?>(CustomerTelemetry.Tag, "acme"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("  ")]
        public void Null_or_empty_id_opens_no_scope_and_does_not_throw(string? customerId)
        {
            using var activity = _source.StartActivity("work");

            var scope = CustomerTelemetry.BeginScope(_logger, customerId);

            scope.ShouldBeNull();
            _logger.Scopes.ShouldBeEmpty();
            activity!.GetTagItem(CustomerTelemetry.Tag).ShouldBeNull();
        }

        private sealed class ScopeRecordingLogger : ILogger
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
}
