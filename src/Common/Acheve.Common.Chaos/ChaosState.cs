using System.Diagnostics;
using System.Diagnostics.Metrics;
using Acheve.Common.Shared;
using Microsoft.Extensions.Logging;

namespace Acheve.Common.Chaos
{
    /// <summary>
    /// The chaos settings in force in this service. They change at run time through the /chaos endpoints.
    /// Also counts every injection (meter Acheve.Chaos) and tags the current span with it.
    /// </summary>
    public sealed partial class ChaosState
    {
        public const string InjectedTag = "chaos.injected";

        // Name of the settings set through PUT /chaos.
        public const string CustomScenario = "custom";

        private static readonly string[] ScenarioNames = [.. ChaosSettings.Scenarios.Keys, CustomScenario];

        private readonly ILogger<ChaosState> _logger;
        private readonly Counter<long> _injections;
        private ChaosSettings _current;

        public ChaosState(ChaosSettings initial, IMeterFactory meterFactory, ILogger<ChaosState> logger, Func<double>? random = null)
        {
            _current = initial;
            _logger = logger;
            Random = random ?? System.Random.Shared.NextDouble;

            var meter = meterFactory.Create(Constants.Telemetry.ChaosMeterName);

            _injections = meter.CreateCounter<long>(
                "chaos.injections",
                unit: "{injection}",
                description: "Faults, latency and callback misbehaviour injected by the chaos.");

            // 1 for the scenario in force and 0 for the rest, so dashboards can show when each one was on.
            // Reporting the 0s too keeps an old scenario from looking active until its series goes stale.
            meter.CreateObservableGauge(
                "chaos.scenario.active",
                () => ScenarioNames.Select(name => new Measurement<int>(
                    name == Current.Scenario ? 1 : 0,
                    new KeyValuePair<string, object?>("chaos.scenario", name))),
                unit: "{scenario}",
                description: "1 for the chaos scenario in force, 0 for the others.");
        }

        public ChaosSettings Current => Volatile.Read(ref _current);

        /// <summary>Returns a number between 0 and 1; tests replace it to decide every injection.</summary>
        internal Func<double> Random { get; }

        public void Set(ChaosSettings settings)
        {
            var previous = Interlocked.Exchange(ref _current, settings);

            // The chaos monkey sets every service at each step; only real changes are worth a log line
            // (the Chaos run dashboard turns these lines into annotations).
            if (previous != settings)
            {
                LogScenarioChanged(previous.Scenario, settings.Scenario, settings);
            }
        }

        public bool TrySetScenario(string name)
        {
            if (!ChaosSettings.Scenarios.TryGetValue(name, out var settings))
            {
                return false;
            }

            Set(settings);
            return true;
        }

        /// <summary>Decides what to do with one callback of an external system.</summary>
        public CallbackPlan PlanCallback()
        {
            var settings = Current;

            if (Roll(settings.CallbackDropRate))
            {
                RecordInjection("callback_drop");
                return new CallbackPlan(Drop: true, Duplicate: false, settings.CallbackDelay);
            }

            var duplicate = Roll(settings.CallbackDuplicateRate);

            if (duplicate)
            {
                RecordInjection("callback_duplicate");
            }

            if (settings.CallbackDelay > TimeSpan.Zero)
            {
                RecordInjection("callback_delay");
            }

            return new CallbackPlan(Drop: false, duplicate, settings.CallbackDelay);
        }

        internal void RecordInjection(string kind)
        {
            var scenario = Current.Scenario;

            _injections.Add(
                1,
                new KeyValuePair<string, object?>("chaos.kind", kind),
                new KeyValuePair<string, object?>("chaos.scenario", scenario));

            Activity.Current?.SetTag(InjectedTag, kind);

            LogInjected(kind, scenario);
        }

        private bool Roll(double rate) => rate > 0 && Random() < rate;

        [LoggerMessage(LogLevel.Warning, "Chaos scenario changed from {previous} to {scenario}: {settings}")]
        private partial void LogScenarioChanged(string previous, string scenario, ChaosSettings settings);

        [LoggerMessage(LogLevel.Information, "Chaos injected {kind} (scenario {scenario})")]
        private partial void LogInjected(string kind, string scenario);
    }

    /// <param name="Drop">Never call back.</param>
    /// <param name="Duplicate">Call back twice.</param>
    /// <param name="Delay">Wait this much longer before calling back.</param>
    public readonly record struct CallbackPlan(bool Drop, bool Duplicate, TimeSpan Delay);
}
