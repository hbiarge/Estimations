namespace Acheve.Application.ProcessManager.Handlers
{
    // Creates the case metrics when the host starts. They are a singleton that only the saga asks for, so without this
    // they would be created while the first message is handled, and the series that start at 0 would be sent in the
    // same export as the first increment: Prometheus would never see that first case.
    public sealed class EstimationMetricsInitializer(EstimationMetrics metrics) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken)
        {
            _ = metrics;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
