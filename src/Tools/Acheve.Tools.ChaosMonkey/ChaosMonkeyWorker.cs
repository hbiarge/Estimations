using Acheve.Common.Shared;
using Microsoft.Extensions.Options;

namespace Acheve.Tools.ChaosMonkey
{
    /// <summary>
    /// Walks the chaos plan: at each step switches every target to its scenario (healthy when the step doesn't name it),
    /// then waits the step's duration. Puts every target back to healthy when the plan ends or the monkey stops.
    /// </summary>
    internal sealed partial class ChaosMonkeyWorker(
        IChaosClient client,
        ChaosMonkeyMetrics metrics,
        IOptions<ChaosMonkeyOptions> options,
        TimeProvider time,
        Random random,
        ILogger<ChaosMonkeyWorker> logger) : BackgroundService
    {
        private static readonly TimeSpan ResetTimeout = TimeSpan.FromSeconds(15);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Until the strategy runner replaces this worker, it runs the tour.
            var strategy = options.Value.Strategies[Constants.ChaosMonkey.Tour];
            LogStarting(Constants.ChaosMonkey.Tour, strategy.Repeat);

            try
            {
                var number = 0;

                foreach (var step in ChaosPlan.Create(strategy, random))
                {
                    number++;
                    metrics.StepStarted();
                    LogStep(number, step.ToString());

                    await ApplyAsync(step.ScenarioFor, stoppingToken);
                    await Task.Delay(step.Duration, time, stoppingToken);
                }

                LogFinished();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            finally
            {
                // Leave nothing broken behind, even when the host is stopping.
                using var reset = new CancellationTokenSource(ResetTimeout);
                await ApplyAsync(_ => Constants.Chaos.Healthy, reset.Token);
                LogReset();
            }
        }

        private Task ApplyAsync(Func<string, string> scenarioFor, CancellationToken cancellationToken) =>
            Task.WhenAll(ChaosTargets.Names.Select(async target =>
            {
                var scenario = scenarioFor(target);
                var succeeded = await client.SetScenarioAsync(target, scenario, cancellationToken);

                metrics.Switched(target, scenario, succeeded);

                if (!succeeded)
                {
                    LogSwitchFailed(target, scenario);
                }
            }));

        [LoggerMessage(LogLevel.Information, "Chaos monkey starting: {strategy} strategy (repeat: {repeat}).")]
        private partial void LogStarting(string strategy, bool repeat);

        [LoggerMessage(LogLevel.Warning, "Chaos step {number}: {step}")]
        private partial void LogStep(int number, string step);

        [LoggerMessage(LogLevel.Warning, "Couldn't switch {target} to {scenario}; is it running?")]
        private partial void LogSwitchFailed(string target, string scenario);

        [LoggerMessage(LogLevel.Information, "Chaos plan finished.")]
        private partial void LogFinished();

        [LoggerMessage(LogLevel.Information, "Every target is back to healthy.")]
        private partial void LogReset();
    }
}
