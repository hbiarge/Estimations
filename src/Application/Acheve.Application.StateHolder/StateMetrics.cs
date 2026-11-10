using System.Diagnostics.Metrics;
using Acheve.Common.Shared;

namespace Acheve.Application.StateHolder;

// Gauges read from the state of every case: how many are running and how old the oldest one is.
// The state lives in memory, so both start from zero when the StateHolder restarts.
public sealed class StateMetrics
{
    public StateMetrics(IMeterFactory meterFactory, State state)
    {
        var meter = meterFactory.Create(Constants.Telemetry.MeterName);

        meter.CreateObservableGauge(
            "estimations.cases.active",
            () => state.ActiveCasesByState().Select(x => new Measurement<int>(
                x.Value,
                new KeyValuePair<string, object?>("estimation.state", x.Key.ToString()))),
            "{case}",
            "Running cases per state.");

        meter.CreateObservableGauge(
            "estimations.cases.oldest_active.age",
            () => state.OldestActiveCaseAge().TotalSeconds,
            "s",
            "Age of the oldest running case.");
    }
}
