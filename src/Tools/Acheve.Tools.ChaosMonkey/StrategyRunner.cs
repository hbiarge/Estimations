using Acheve.Common.Shared;
using Microsoft.Extensions.Options;

namespace Acheve.Tools.ChaosMonkey
{
    /// <summary>The strategy running and its current step (1-based); all null when idle.</summary>
    internal sealed record StrategyStatus(string? Strategy, int? Step, string? StepName)
    {
        public static StrategyStatus Idle { get; } = new(null, null, null);
    }

    /// <summary>
    /// Runs at most one strategy at a time, in the background. At each step it switches every chaos target to its
    /// scenario (healthy when the step doesn't name it) and every caller to its resilience profile (standard when the
    /// step doesn't name it), then waits the step's duration, unless told to skip to the next step.
    /// When a strategy finishes, is stopped or is replaced, and when the host stops, it puts everything back
    /// to healthy and standard.
    /// </summary>
    internal sealed partial class StrategyRunner(
        IChaosClient client,
        ChaosMonkeyMetrics metrics,
        IOptions<ChaosMonkeyOptions> options,
        TimeProvider time,
        Random random,
        ILogger<StrategyRunner> logger) : IHostedService
    {
        private static readonly TimeSpan ResetTimeout = TimeSpan.FromSeconds(15);

        private static readonly ChaosStep Defaults = new("Reset", TimeSpan.Zero, new Dictionary<string, string>());

        // Lets one start or cancel finish (including the reset of the strategy it replaces) before the next one begins.
        private readonly SemaphoreSlim _gate = new(1, 1);

        // Guards _run, _status and the current run's step token.
        private readonly Lock _sync = new();

        private Run? _run;
        private StrategyStatus _status = StrategyStatus.Idle;

        private sealed class Run(string strategy)
        {
            public string Strategy { get; } = strategy;

            public CancellationTokenSource Cancellation { get; } = new();

            public CancellationTokenSource? Step { get; set; }

            public Task Task { get; set; } = Task.CompletedTask;
        }

        public StrategyStatus Status
        {
            get
            {
                lock (_sync)
                {
                    return _status;
                }
            }
        }

        /// <summary>The run of the last strategy started, until it ends (and has reset everything).</summary>
        public Task Completion
        {
            get
            {
                lock (_sync)
                {
                    return _run?.Task ?? Task.CompletedTask;
                }
            }
        }

        /// <summary>
        /// Stops the strategy running, if any, waits for its reset, and starts this one in the background.
        /// False when there is no strategy with that name.
        /// </summary>
        public async Task<bool> RunAsync(string strategy, CancellationToken cancellationToken)
        {
            if (!options.Value.Strategies.TryGetValue(strategy, out var settings))
            {
                return false;
            }

            await _gate.WaitAsync(cancellationToken);

            try
            {
                await StopRunningAsync();

                var run = new Run(strategy);

                lock (_sync)
                {
                    _run = run;
                    _status = new StrategyStatus(strategy, null, null);
                    run.Task = Task.Run(() => ExecuteAsync(run, settings), CancellationToken.None);
                }

                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Stops the strategy running, if any, and waits until everything is back to healthy and standard.</summary>
        public async Task CancelAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);

            try
            {
                await StopRunningAsync();
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Ends the current step's wait now: the strategy goes on to its next step, or finishes after the last one.</summary>
        public void SkipStep()
        {
            lock (_sync)
            {
                _run?.Step?.Cancel();
            }
        }

        Task IHostedService.StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        Task IHostedService.StopAsync(CancellationToken cancellationToken) => CancelAsync(cancellationToken);

        private async Task StopRunningAsync()
        {
            Run? run;

            lock (_sync)
            {
                run = _run;
            }

            if (run is null)
            {
                return;
            }

            await run.Cancellation.CancelAsync();
            await run.Task;
        }

        private async Task ExecuteAsync(Run run, StrategyOptions settings)
        {
            var stopping = run.Cancellation.Token;
            LogStarted(run.Strategy);

            try
            {
                var number = 0;

                foreach (var step in ChaosPlan.Create(settings, random))
                {
                    stopping.ThrowIfCancellationRequested();
                    number++;

                    using var skip = CancellationTokenSource.CreateLinkedTokenSource(stopping);

                    lock (_sync)
                    {
                        // Created before the step is applied, so a skip while switching ends this step's wait.
                        run.Step = skip;
                        _status = new StrategyStatus(run.Strategy, number, step.Name);
                    }

                    try
                    {
                        metrics.StepStarted(run.Strategy);
                        LogStep(number, run.Strategy, step.ToString());

                        await ApplyAsync(step, stopping);

                        try
                        {
                            await Task.Delay(step.Duration, time, skip.Token);
                        }
                        catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
                        {
                            // Skipped: on to the next step.
                        }
                    }
                    finally
                    {
                        // Before skip is disposed, so SkipStep never cancels a disposed source.
                        lock (_sync)
                        {
                            run.Step = null;
                        }
                    }
                }

                LogFinished(run.Strategy);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                LogStopped(run.Strategy);
            }
            catch (Exception exception)
            {
                LogFailed(exception, run.Strategy);
            }
            finally
            {
                lock (_sync)
                {
                    run.Step = null;

                    if (_run == run)
                    {
                        _status = StrategyStatus.Idle;
                    }
                }

                // Leave nothing broken behind, even when the host is stopping. A reset that can't finish is logged;
                // it must not fault the run, or every later start and stop would rethrow it.
                try
                {
                    using var reset = new CancellationTokenSource(ResetTimeout);
                    await ApplyAsync(Defaults, reset.Token);
                    LogReset();
                }
                catch (OperationCanceledException)
                {
                    LogResetTimedOut(ResetTimeout);
                }
            }
        }

        private Task ApplyAsync(ChaosStep step, CancellationToken cancellationToken) =>
            Task.WhenAll(
                ChaosTargets.Names
                    .Select(target => SwitchAsync(ChaosMonkeyMetrics.ChaosKind, target, step.ScenarioFor(target), client.SetScenarioAsync, cancellationToken))
                    .Concat(ResilienceTargets.Names
                        .Select(caller => SwitchAsync(ChaosMonkeyMetrics.ResilienceKind, caller, step.ProfileFor(caller), client.SetProfileAsync, cancellationToken))));

        private async Task SwitchAsync(
            string kind,
            string target,
            string value,
            Func<string, string, CancellationToken, Task<bool>> send,
            CancellationToken cancellationToken)
        {
            bool succeeded;

            try
            {
                succeeded = await send(target, value, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // One target failing doesn't stop the others, or the step.
                LogSwitchError(exception, target, value);
                succeeded = false;
            }

            metrics.Switched(kind, target, value, succeeded);

            if (!succeeded)
            {
                LogSwitchFailed(target, value);
            }
        }

        [LoggerMessage(LogLevel.Information, "Strategy {strategy} started.")]
        private partial void LogStarted(string strategy);

        [LoggerMessage(LogLevel.Warning, "Chaos step {number} of {strategy}: {step}")]
        private partial void LogStep(int number, string strategy, string step);

        [LoggerMessage(LogLevel.Warning, "Couldn't switch {target} to {value}; is it running?")]
        private partial void LogSwitchFailed(string target, string value);

        [LoggerMessage(LogLevel.Warning, "Couldn't switch {target} to {value}.")]
        private partial void LogSwitchError(Exception exception, string target, string value);

        [LoggerMessage(LogLevel.Error, "The reset didn't finish within {timeout}; some services may still be in chaos or off standard.")]
        private partial void LogResetTimedOut(TimeSpan timeout);

        [LoggerMessage(LogLevel.Information, "Strategy {strategy} finished.")]
        private partial void LogFinished(string strategy);

        [LoggerMessage(LogLevel.Information, "Strategy {strategy} stopped.")]
        private partial void LogStopped(string strategy);

        [LoggerMessage(LogLevel.Error, "Strategy {strategy} failed.")]
        private partial void LogFailed(Exception exception, string strategy);

        [LoggerMessage(LogLevel.Information, "Every target is back to healthy and every caller to standard.")]
        private partial void LogReset();
    }
}
