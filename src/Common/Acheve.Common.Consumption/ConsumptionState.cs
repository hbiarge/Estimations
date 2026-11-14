using System.Diagnostics.Metrics;
using Acheve.Common.Shared;
using Microsoft.Extensions.Logging;
using Rebus.Bus.Advanced;

namespace Acheve.Common.Consumption
{
    /// <summary>
    /// Whether this service's Rebus worker takes messages from its queue. Pausing sets Rebus's workers to 0, so the
    /// messages wait (ready) in RabbitMQ while the process and its health checks stay up; resuming restores the count.
    /// </summary>
    public sealed partial class ConsumptionState
    {
        private readonly Func<IWorkersApi> _workers;
        private readonly ILogger<ConsumptionState> _logger;

        // One pause or resume at a time: Rebus blocks until the workers have stopped, after their current message.
        private readonly SemaphoreSlim _gate = new(1, 1);

        private int _pausedWorkers;
        private volatile bool _paused;

        /// <param name="workers">Resolved on first use, so the bus doesn't have to exist when the state is created.</param>
        public ConsumptionState(Func<IWorkersApi> workers, IMeterFactory meterFactory, ILogger<ConsumptionState> logger)
        {
            _workers = workers;
            _logger = logger;

            var meter = meterFactory.Create(Constants.Telemetry.ConsumptionMeterName);

            meter.CreateObservableGauge(
                "consumption.paused",
                () => _paused ? 1 : 0,
                unit: "{state}",
                description: "1 while the service's message consumption is paused, 0 while it runs.");
        }

        public bool IsPaused => _paused;

        public string State => _paused ? Constants.Consumption.Paused : Constants.Consumption.Running;

        /// <summary>Stops taking messages. Waits for the messages being handled to finish. Pausing again does nothing.</summary>
        public async Task PauseAsync()
        {
            await _gate.WaitAsync();

            try
            {
                if (_paused)
                {
                    return;
                }

                var workers = _workers();
                var count = workers.Count;

                await Task.Run(() => workers.SetNumberOfWorkers(0));

                _pausedWorkers = count;
                _paused = true;
                LogPaused(count);
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Takes messages again with the workers it had before the pause. Resuming while running does nothing.</summary>
        public async Task ResumeAsync()
        {
            await _gate.WaitAsync();

            try
            {
                if (!_paused)
                {
                    return;
                }

                var workers = _workers();
                await Task.Run(() => workers.SetNumberOfWorkers(_pausedWorkers));

                _paused = false;
                LogResumed(_pausedWorkers);
            }
            finally
            {
                _gate.Release();
            }
        }

        // Warnings, like the chaos and resilience switches: the Chaos run dashboard turns these lines into annotations.
        [LoggerMessage(LogLevel.Warning, "Message consumption paused ({workers} workers stopped)")]
        private partial void LogPaused(int workers);

        [LoggerMessage(LogLevel.Warning, "Message consumption resumed ({workers} workers)")]
        private partial void LogResumed(int workers);
    }
}
