using System.Diagnostics.Metrics;
using Acheve.Common.Shared;

namespace Acheve.Tools.TrafficGenerator
{
    /// <summary>The client-side view of the cases (meter Acheve.Traffic).</summary>
    internal sealed class TrafficMetrics
    {
        private const string OutcomeTag = "traffic.outcome";

        private readonly Counter<long> _started;
        private readonly Counter<long> _finished;
        private readonly Counter<long> _skipped;
        private readonly Histogram<double> _duration;
        private readonly UpDownCounter<long> _inFlight;

        public TrafficMetrics(IMeterFactory meterFactory)
        {
            var meter = meterFactory.Create(Constants.Telemetry.TrafficMeterName);

            _started = meter.CreateCounter<long>(
                "traffic.cases.started", unit: "{case}", description: "Cases the traffic generator asked for.");
            _finished = meter.CreateCounter<long>(
                "traffic.cases.finished", unit: "{case}", description: "Cases that ended, by outcome as the client sees it.");
            _skipped = meter.CreateCounter<long>(
                "traffic.cases.skipped", unit: "{case}", description: "Cases not started because too many were still in flight.");
            _duration = meter.CreateHistogram<double>(
                "traffic.case.duration", unit: "s", description: "From the Api accepting the case to its final state, by outcome.",
                advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [5, 10, 15, 20, 30, 45, 60, 90, 120, 180, 300, 600] });
            _inFlight = meter.CreateUpDownCounter<long>(
                "traffic.cases.in_flight", unit: "{case}", description: "Cases started and not finished yet.");
            TargetRate = 0;
            meter.CreateObservableGauge(
                "traffic.rate.target", () => TargetRate, unit: "{case}/min", description: "Cases per minute the generator aims for right now.");
        }

        public double TargetRate { get; set; }

        public void Started()
        {
            _started.Add(1);
            _inFlight.Add(1);
        }

        public void Finished(CaseOutcome outcome, TimeSpan? duration)
        {
            var tag = new KeyValuePair<string, object?>(OutcomeTag, ToTagValue(outcome));

            _finished.Add(1, tag);
            _inFlight.Add(-1);

            if (duration is { } value)
            {
                _duration.Record(value.TotalSeconds, tag);
            }
        }

        public void Skipped() => _skipped.Add(1);

        // snake_case, like the other metric tag values.
        internal static string ToTagValue(CaseOutcome outcome) => outcome switch
        {
            CaseOutcome.NotificationFailed => "notification_failed",
            CaseOutcome.TimedOut => "timed_out",
            _ => outcome.ToString().ToLowerInvariant()
        };
    }
}
