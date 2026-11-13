using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Acheve.Common.Resilience
{
    /// <summary>
    /// One Polly pipeline per HttpClient, built from the settings in force and rebuilt when they change.
    /// It lives here and not in the handler: IHttpClientFactory recycles handlers every two minutes,
    /// and that would close an open circuit breaker.
    /// </summary>
    public sealed class ResiliencePipelines(ResilienceState state, ILoggerFactory loggerFactory, TimeProvider timeProvider)
    {
        private readonly Dictionary<string, (ResilienceSettings Settings, ResiliencePipeline<HttpResponseMessage> Pipeline)> _pipelines = [];
        private readonly Lock _lock = new();

        public ResiliencePipeline<HttpResponseMessage> Get(string clientName)
        {
            var settings = state.Current;

            lock (_lock)
            {
                // Same settings, same pipeline: the circuit breaker keeps its state between requests.
                if (_pipelines.TryGetValue(clientName, out var entry) && entry.Settings == settings)
                {
                    return entry.Pipeline;
                }

                var pipeline = Build(clientName, settings, loggerFactory, timeProvider);
                _pipelines[clientName] = (settings, pipeline);
                return pipeline;
            }
        }

        /// <summary>Null when the settings can build a pipeline; otherwise why not.</summary>
        public static string? Validate(ResilienceSettings settings)
        {
            if (settings.Retry is not null && settings.Hedging is not null)
            {
                return "Use retry or hedging, not both.";
            }

            if (settings.ConcurrencyLimit is <= 0)
            {
                return "The concurrency limit must be over 0.";
            }

            try
            {
                Build("validation", settings, NullLoggerFactory.Instance, TimeProvider.System);
                return null;
            }
            catch (ValidationException e)
            {
                return e.Message;
            }
        }

        internal static ResiliencePipeline<HttpResponseMessage> Build(
            string clientName,
            ResilienceSettings settings,
            ILoggerFactory loggerFactory,
            TimeProvider timeProvider)
        {
            // Name and instance become the pipeline.name and pipeline.instance tags of Polly's metrics.
            var builder = new ResiliencePipelineBuilder<HttpResponseMessage>
            {
                Name = clientName,
                InstanceName = settings.Profile,
                TimeProvider = timeProvider
            };

            if (settings.ConcurrencyLimit is { } permitLimit)
            {
                builder.AddConcurrencyLimiter(permitLimit, queueLimit: 0);
            }

            if (settings.TotalTimeout is { } totalTimeout)
            {
                builder.AddTimeout(new TimeoutStrategyOptions { Name = "TotalRequestTimeout", Timeout = totalTimeout });
            }

            if (settings.Retry is { } retry)
            {
                builder.AddRetry(new HttpRetryStrategyOptions
                {
                    Name = "Retry",
                    MaxRetryAttempts = retry.MaxRetryAttempts,
                    Delay = retry.BaseDelay,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true
                });
            }

            if (settings.Hedging is { } hedging)
            {
                // Each hedged attempt needs its own copy of the request: a request can only be sent once.
                builder.AddStrategy(_ => new RequestSnapshotStrategy(), new RequestSnapshotStrategyOptions());
                builder.AddHedging(new HttpHedgingStrategyOptions
                {
                    Name = "Hedging",
                    MaxHedgedAttempts = hedging.MaxHedgedAttempts,
                    Delay = hedging.Delay,
                    ActionGenerator = RequestSnapshotStrategy.HedgedAction
                });
            }

            if (settings.CircuitBreaker is { } circuitBreaker)
            {
                builder.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    Name = "CircuitBreaker",
                    FailureRatio = circuitBreaker.FailureRatio,
                    MinimumThroughput = circuitBreaker.MinimumThroughput,
                    SamplingDuration = circuitBreaker.SamplingDuration,
                    BreakDuration = circuitBreaker.BreakDuration
                });
            }

            if (settings.AttemptTimeout is { } attemptTimeout)
            {
                builder.AddTimeout(new TimeoutStrategyOptions { Name = "AttemptTimeout", Timeout = attemptTimeout });
            }

            // Polly's meter (retries, hedges, circuit breaker changes, timeouts) and a log line for each event.
            builder.ConfigureTelemetry(loggerFactory);

            return builder.Build();
        }
    }
}
