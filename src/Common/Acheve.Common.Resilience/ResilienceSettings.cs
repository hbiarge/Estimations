using Acheve.Common.Shared;

namespace Acheve.Common.Resilience
{
    /// <summary>
    /// The resilience strategies an HttpClient uses right now. A strategy left null is off.
    /// They run in this order, from the outside in: concurrency limit, total timeout, retry or hedging,
    /// circuit breaker and attempt timeout (the same order as AddStandardResilienceHandler()).
    /// </summary>
    public sealed record ResilienceSettings
    {
        public string Profile { get; init; } = Constants.Resilience.Standard;

        /// <summary>Requests that can run at the same time (a bulkhead). Requests over the limit are rejected at once.</summary>
        public int? ConcurrencyLimit { get; init; }

        /// <summary>Limit for the whole call, every retry or hedged attempt included.</summary>
        public TimeSpan? TotalTimeout { get; init; }

        /// <summary>Retries transient failures (5xx, 408, 429, network errors, timeouts). Can't be used with <see cref="Hedging"/>.</summary>
        public RetrySettings? Retry { get; init; }

        /// <summary>Sends another attempt when the first is slow or fails, and takes the first good answer. Can't be used with <see cref="Retry"/>.</summary>
        public HedgingSettings? Hedging { get; init; }

        /// <summary>Stops calling for a while when too many calls fail.</summary>
        public CircuitBreakerSettings? CircuitBreaker { get; init; }

        /// <summary>Limit for each attempt.</summary>
        public TimeSpan? AttemptTimeout { get; init; }

        /// <summary>The named profiles, as the Aspire dashboard commands use them.</summary>
        public static IReadOnlyDictionary<string, ResilienceSettings> Profiles { get; } = new Dictionary<string, ResilienceSettings>
        {
            // The bare HttpClient: every fault reaches the handler.
            [Constants.Resilience.None] = new()
            {
                Profile = Constants.Resilience.None
            },
            // Retries and nothing else: hides flaky faults, but against an outage every call becomes four.
            [Constants.Resilience.RetryOnly] = new()
            {
                Profile = Constants.Resilience.RetryOnly,
                Retry = new RetrySettings()
            },
            // What AddStandardResilienceHandler() does with its default options.
            [Constants.Resilience.Standard] = new()
            {
                Profile = Constants.Resilience.Standard,
                ConcurrencyLimit = 1000,
                TotalTimeout = TimeSpan.FromSeconds(30),
                Retry = new RetrySettings(),
                CircuitBreaker = new CircuitBreakerSettings(),
                AttemptTimeout = TimeSpan.FromSeconds(10)
            },
            // No retries, short timeouts and a circuit breaker that opens quickly: fails fast and stops calling a system that is down.
            // 5 calls in 30 s is low enough for the traffic generator's default rate to open it.
            [Constants.Resilience.FailFast] = new()
            {
                Profile = Constants.Resilience.FailFast,
                TotalTimeout = TimeSpan.FromSeconds(5),
                CircuitBreaker = new CircuitBreakerSettings
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(30)
                },
                AttemptTimeout = TimeSpan.FromSeconds(3)
            },
            // Another attempt when the first takes over a second or fails: cuts the slow tail, at the cost of extra calls.
            [Constants.Resilience.Hedging] = new()
            {
                Profile = Constants.Resilience.Hedging,
                TotalTimeout = TimeSpan.FromSeconds(30),
                Hedging = new HedgingSettings(),
                AttemptTimeout = TimeSpan.FromSeconds(10)
            }
        };
    }

    /// <summary>Exponential backoff with jitter.</summary>
    public sealed record RetrySettings
    {
        public int MaxRetryAttempts { get; init; } = 3;

        public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(2);
    }

    public sealed record HedgingSettings
    {
        /// <summary>Attempts sent on top of the first one.</summary>
        public int MaxHedgedAttempts { get; init; } = 2;

        /// <summary>How long to wait for an attempt before sending the next one.</summary>
        public TimeSpan Delay { get; init; } = TimeSpan.FromSeconds(1);
    }

    /// <summary>Opens when <see cref="FailureRatio"/> of at least <see cref="MinimumThroughput"/> calls in <see cref="SamplingDuration"/> fail.</summary>
    public sealed record CircuitBreakerSettings
    {
        public double FailureRatio { get; init; } = 0.1;

        public int MinimumThroughput { get; init; } = 100;

        public TimeSpan SamplingDuration { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>How long the circuit stays open before it lets a trial call through.</summary>
        public TimeSpan BreakDuration { get; init; } = TimeSpan.FromSeconds(5);
    }
}
