using System.Diagnostics.Metrics;
using Acheve.Common.Shared;

namespace Acheve.Tools.ChaosMonkey
{
    /// <summary>
    /// Meter Acheve.ChaosMonkey. What each service is really in comes from the services themselves
    /// (chaos.scenario.active, resilience.profile.active).
    /// </summary>
    internal sealed class ChaosMonkeyMetrics
    {
        public const string ChaosKind = "chaos";
        public const string ResilienceKind = "resilience";
        public const string ConsumptionKind = "consumption";

        private readonly Counter<long> _switches;
        private readonly Counter<long> _steps;

        public ChaosMonkeyMetrics(IMeterFactory meterFactory)
        {
            var meter = meterFactory.Create(Constants.Telemetry.ChaosMonkeyMeterName);

            _switches = meter.CreateCounter<long>(
                "chaos.monkey.switches",
                unit: "{switch}",
                description: "Chaos scenario, resilience profile and consumption switches sent to the services, by kind, target, value and result.");
            _steps = meter.CreateCounter<long>(
                "chaos.monkey.steps", unit: "{step}", description: "Strategy steps started, by strategy.");
        }

        public void StepStarted(string strategy) => _steps.Add(1, new KeyValuePair<string, object?>("chaos.strategy", strategy));

        /// <param name="kind"><see cref="ChaosKind"/>, <see cref="ResilienceKind"/> or <see cref="ConsumptionKind"/>.</param>
        public void Switched(string kind, string target, string value, bool succeeded) => _switches.Add(
            1,
            new KeyValuePair<string, object?>("chaos.kind", kind),
            new KeyValuePair<string, object?>("chaos.target", target),
            new KeyValuePair<string, object?>("chaos.value", value),
            new KeyValuePair<string, object?>("chaos.result", succeeded ? "ok" : "failed"));
    }
}
