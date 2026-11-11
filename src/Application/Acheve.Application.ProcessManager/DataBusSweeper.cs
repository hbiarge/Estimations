using Rebus.Bus;
using Rebus.DataBus;

namespace Acheve.Application.ProcessManager
{
    /// <summary>
    /// Deletes data bus attachments older than the retention time. The saga deletes a case's images and analyses
    /// when the case ends; this removes the rest: estimations kept for the client to read, and attachments
    /// no saga knows about (callbacks for a case that already ended, messages that ended in the error queue).
    /// </summary>
    public sealed class DataBusSweeper(
        IBus bus,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<DataBusSweeper> logger) : BackgroundService
    {
        // Must be longer than the longest case, so no attachment of a running case is deleted.
        private readonly TimeSpan _retention = configuration.GetValue("DataBus:Retention", TimeSpan.FromHours(24));
        private readonly TimeSpan _interval = configuration.GetValue("DataBus:SweepInterval", TimeSpan.FromMinutes(15));

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var timer = new PeriodicTimer(_interval, timeProvider);

            do
            {
                try
                {
                    await Sweep(stoppingToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "The data bus sweep failed. It runs again in {interval}.", _interval);
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }

        private async Task Sweep(CancellationToken cancellationToken)
        {
            var dataBus = bus.Advanced.DataBus;
            var olderThan = timeProvider.GetUtcNow() - _retention;
            var deleted = 0;

            foreach (var ticket in dataBus.Query(saveTime: new TimeRange(to: olderThan)).ToList())
            {
                cancellationToken.ThrowIfCancellationRequested();

                await dataBus.Delete(ticket);
                deleted++;
            }

            if (deleted > 0)
            {
                logger.LogInformation(
                    "Deleted {count} data bus attachment(s) older than {retention}.", deleted, _retention);
            }
        }
    }
}
