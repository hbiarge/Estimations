using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>Starts cases at the configured rate and pattern, each one followed by a <see cref="CaseRunner"/>.</summary>
    internal sealed partial class TrafficWorker(
        CaseRunner runner,
        CaseFactory factory,
        TrafficMetrics metrics,
        IOptions<TrafficOptions> options,
        TimeProvider time,
        Random random,
        ILogger<TrafficWorker> logger) : BackgroundService
    {
        // Longest single wait, so a change of rate (Wave pattern) is picked up soon.
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(5);

        private readonly ConcurrentDictionary<Task, byte> _inFlight = new();

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var settings = options.Value;
            var start = time.GetUtcNow();
            DateTimeOffset? nextBurst = settings.Pattern == TrafficPattern.Bursts ? start + settings.BurstEvery : null;
            DateTimeOffset? nextCase = null;

            LogStarting(settings.CasesPerMinute, settings.Pattern);

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var now = time.GetUtcNow();
                    var rate = TrafficShape.CasesPerMinute(settings, now - start);
                    metrics.TargetRate = rate;

                    if (nextBurst is { } burst && now >= burst)
                    {
                        LogBurst(settings.BurstSize);

                        for (var i = 0; i < settings.BurstSize; i++)
                        {
                            Launch(stoppingToken);
                        }

                        nextBurst = burst + settings.BurstEvery;
                        continue;
                    }

                    nextCase ??= TrafficShape.NextGap(rate, random) is { } gap ? now + gap : null;

                    if (nextCase is { } due && now >= due)
                    {
                        Launch(stoppingToken);
                        nextCase = null;
                        continue;
                    }

                    var wait = MaxWait;

                    if (nextCase is { } dueCase && dueCase - now < wait)
                    {
                        wait = dueCase - now;
                    }

                    if (nextBurst is { } dueBurst && dueBurst - now < wait)
                    {
                        wait = dueBurst - now;
                    }

                    await Task.Delay(wait, time, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }

            // Cases still running are cancelled with the host; they don't count as finished.
            await Task.WhenAll(_inFlight.Keys).ContinueWith(_ => { }, TaskScheduler.Default);
        }

        private void Launch(CancellationToken stoppingToken)
        {
            if (_inFlight.Count >= options.Value.MaxCasesInFlight)
            {
                metrics.Skipped();
                return;
            }

            var task = Task.Run(async () =>
            {
                try
                {
                    await runner.RunAsync(factory.Create(), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    LogCaseCrashed(ex);
                }
            }, CancellationToken.None);

            _inFlight.TryAdd(task, 0);
            task.ContinueWith(t => _inFlight.TryRemove(t, out _), TaskScheduler.Default);
        }

        [LoggerMessage(LogLevel.Information, "Generating traffic: {casesPerMinute} cases per minute, {pattern} pattern.")]
        private partial void LogStarting(double casesPerMinute, TrafficPattern pattern);

        [LoggerMessage(LogLevel.Information, "Burst of {burstSize} cases.")]
        private partial void LogBurst(int burstSize);

        [LoggerMessage(LogLevel.Error, "A case failed unexpectedly.")]
        private partial void LogCaseCrashed(Exception ex);
    }
}
