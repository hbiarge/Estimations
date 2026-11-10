using Acheve.Common.Shared;

namespace Acheve.Common.Chaos
{
    /// <summary>
    /// What the chaos injects right now. Rates go from 0 (never) to 1 (always).
    /// The callback settings only apply to the external systems that call back.
    /// </summary>
    public sealed record ChaosSettings
    {
        public string Scenario { get; init; } = Constants.Chaos.Healthy;

        /// <summary>Share of requests answered straight away with <see cref="FaultStatusCode"/>.</summary>
        public double FaultRate { get; init; }

        public int FaultStatusCode { get; init; } = 503;

        /// <summary>Share of requests that wait <see cref="Latency"/> before they run.</summary>
        public double LatencyRate { get; init; }

        public TimeSpan Latency { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>Share of accepted jobs that never call back.</summary>
        public double CallbackDropRate { get; init; }

        /// <summary>Share of callbacks sent twice.</summary>
        public double CallbackDuplicateRate { get; init; }

        /// <summary>Extra time before every callback, on top of the normal processing time.</summary>
        public TimeSpan CallbackDelay { get; init; }

        public static ChaosSettings Healthy { get; } = new();

        /// <summary>The named scenarios, as the Aspire dashboard commands and the chaos monkey use them.</summary>
        public static IReadOnlyDictionary<string, ChaosSettings> Scenarios { get; } = new Dictionary<string, ChaosSettings>
        {
            [Constants.Chaos.Healthy] = Healthy,
            // Mostly hidden by the callers' HTTP retries; shows up as retries and latency.
            [Constants.Chaos.Flaky] = new()
            {
                Scenario = Constants.Chaos.Flaky,
                FaultRate = 0.3,
                LatencyRate = 0.2,
                Latency = TimeSpan.FromSeconds(2)
            },
            // Every request is slow (still under the callers' 10 s attempt timeout) and callbacks come late.
            [Constants.Chaos.Slow] = new()
            {
                Scenario = Constants.Chaos.Slow,
                LatencyRate = 1,
                Latency = TimeSpan.FromSeconds(5),
                CallbackDelay = TimeSpan.FromSeconds(20)
            },
            // An outage: every request fails, even after retries.
            [Constants.Chaos.Down] = new()
            {
                Scenario = Constants.Chaos.Down,
                FaultRate = 1
            },
            // Accepts the work but forgets half of the callbacks: the saga's wait timeouts handle it.
            [Constants.Chaos.LostCallbacks] = new()
            {
                Scenario = Constants.Chaos.LostCallbacks,
                CallbackDropRate = 0.5
            },
            // Calls back twice for half of the jobs: the saga must ignore the second one.
            [Constants.Chaos.DuplicateCallbacks] = new()
            {
                Scenario = Constants.Chaos.DuplicateCallbacks,
                CallbackDuplicateRate = 0.5
            }
        };
    }
}
