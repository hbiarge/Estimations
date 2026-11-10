using System.Diagnostics.Metrics;
using Acheve.Common.Shared;

namespace Acheve.Tools.ChaosMonkey
{
    /// <summary>Meter Acheve.ChaosMonkey. The scenario each service is in comes from the services themselves (chaos.scenario.active).</summary>
    internal sealed class ChaosMonkeyMetrics
    {
        private readonly Counter<long> _switches;
        private readonly Counter<long> _steps;

        public ChaosMonkeyMetrics(IMeterFactory meterFactory)
        {
            var meter = meterFactory.Create(Constants.Telemetry.ChaosMonkeyMeterName);

            _switches = meter.CreateCounter<long>(
                "chaos.monkey.switches", unit: "{switch}", description: "Scenario switches sent to the services, by target, scenario and result.");
            _steps = meter.CreateCounter<long>(
                "chaos.monkey.steps", unit: "{step}", description: "Steps of the chaos plan started.");
        }

        public void StepStarted() => _steps.Add(1);

        public void Switched(string target, string scenario, bool succeeded) => _switches.Add(
            1,
            new KeyValuePair<string, object?>("chaos.target", target),
            new KeyValuePair<string, object?>("chaos.scenario", scenario),
            new KeyValuePair<string, object?>("chaos.result", succeeded ? "ok" : "failed"));
    }
}
