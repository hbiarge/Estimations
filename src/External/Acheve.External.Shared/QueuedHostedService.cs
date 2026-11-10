using System.Runtime.CompilerServices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Acheve.External.Shared
{
    /// <summary>
    /// Runs the queued work items in the background, up to <see cref="BackgroundQueueOptions.MaxConcurrency"/> at a time.
    /// On shutdown it stops taking new items and waits for the running ones.
    /// </summary>
    public sealed class QueuedHostedService(
        IBackgroundTaskQueue taskQueue,
        BackgroundQueueOptions options,
        ILogger<QueuedHostedService> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation(
                "{service} is running {maxConcurrency} work items at a time.",
                nameof(QueuedHostedService),
                options.MaxConcurrency);

            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.MaxConcurrency,
                CancellationToken = stoppingToken
            };

            try
            {
                await Parallel.ForEachAsync(ReadWorkItemsAsync(stoppingToken), parallelOptions, RunWorkItemAsync);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Prevent throwing if stoppingToken was signaled
            }
        }

        private async IAsyncEnumerable<Func<CancellationToken, ValueTask>> ReadWorkItemsAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                yield return await taskQueue.DequeueAsync(cancellationToken);
            }
        }

        private async ValueTask RunWorkItemAsync(
            Func<CancellationToken, ValueTask> workItem,
            CancellationToken cancellationToken)
        {
            try
            {
                await workItem(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Prevent throwing if stoppingToken was signaled
            }
            catch (Exception ex)
            {
                // Caught here so one failing item doesn't stop the others.
                logger.LogError(ex, "Error occurred executing task work item.");
            }
        }

        public override async Task StopAsync(CancellationToken stoppingToken)
        {
            logger.LogInformation(
                $"{nameof(QueuedHostedService)} is stopping.");

            await base.StopAsync(stoppingToken);
        }
    }
}
