using System.Diagnostics;
using System.Diagnostics.Metrics;
using Acheve.Common.Messages;
using Acheve.Common.Shared;

namespace Acheve.Application.ProcessManager.Handlers
{
    // Case-level metrics recorded by the saga. Grafana's Overview and Case flow dashboards are built on them.
    public sealed class EstimationMetrics
    {
        // A case takes about 25 s, and up to about 4 minutes when it waits for timeouts.
        private static readonly double[] DurationBuckets = [1, 2, 5, 10, 15, 20, 25, 30, 45, 60, 90, 120, 180, 240, 300, 600];

        private readonly Counter<long> _casesStarted;
        private readonly Counter<long> _casesFinished;
        private readonly Histogram<double> _caseDuration;
        private readonly Histogram<double> _stateDuration;
        private readonly Counter<long> _imagesFinished;

        public EstimationMetrics(IMeterFactory meterFactory)
        {
            var meter = meterFactory.Create(Constants.Telemetry.MeterName);

            _casesStarted = meter.CreateCounter<long>(
                "estimations.cases.started", "{case}", "Cases started.");

            _casesFinished = meter.CreateCounter<long>(
                "estimations.cases.finished", "{case}", "Cases finished, by result, final state and failure reason.");

            _caseDuration = meter.CreateHistogram(
                "estimations.case.duration", "s", "Time from the request to the end of the case.",
                tags: null, advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

            _stateDuration = meter.CreateHistogram(
                "estimations.case.state.duration", "s", "Time a case spent in a state before moving to the next one.",
                tags: null, advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = DurationBuckets });

            _imagesFinished = meter.CreateCounter<long>(
                "estimations.images.finished", "{image}", "Images with a final result: analysed or failed, and why.");

            StartCountersAtZero();
        }

        // Prometheus can't see the first increment of a counter that appears with a value of 1, so rate() and
        // increase() would miss it. Creating every known series at 0 on startup makes the first case count.
        private void StartCountersAtZero()
        {
            _casesStarted.Add(0);

            foreach (var result in ImageResults.All)
            {
                _imagesFinished.Add(0, new KeyValuePair<string, object?>("estimation.image.result", result));
            }

            foreach (var finalState in new[] { EstimationStates.ClientNotified, EstimationStates.ClientNotificationError })
            {
                _casesFinished.Add(0, FinishedTags(CaseResult.Success, finalState, FailureReasons.None));

                foreach (var reason in FailureReasons.Failures)
                {
                    _casesFinished.Add(0, FinishedTags(CaseResult.Failure, finalState, reason));
                }
            }
        }

        public void CaseStarted() => _casesStarted.Add(1);

        public void CaseFinished(CaseResult result, EstimationStates finalState, string failureReason, TimeSpan? duration)
        {
            var tags = FinishedTags(result, finalState, failureReason);

            _casesFinished.Add(1, tags);

            // Cases started before the saga kept its start time have no duration.
            if (duration is not null)
            {
                _caseDuration.Record(duration.Value.TotalSeconds, tags);
            }
        }

        private static TagList FinishedTags(CaseResult result, EstimationStates finalState, string failureReason) => new()
        {
            { "estimation.result", result == CaseResult.Success ? "success" : "failure" },
            { "estimation.final_state", finalState.ToString() },
            { "estimation.failure_reason", failureReason }
        };

        public void StateLeft(EstimationStates state, TimeSpan duration) =>
            _stateDuration.Record(duration.TotalSeconds, new KeyValuePair<string, object?>("estimation.state", state.ToString()));

        public void ImageFinished(string result) =>
            _imagesFinished.Add(1, new KeyValuePair<string, object?>("estimation.image.result", result));
    }

    public enum CaseResult
    {
        Success,
        Failure
    }

    // Values of the estimation.failure_reason tag.
    public static class FailureReasons
    {
        public const string None = "none";
        public const string NoValidImages = "no_valid_images";
        public const string EstimationRejected = "estimation_rejected";
        public const string EstimationTimeout = "estimation_timeout";

        public static readonly string[] Failures = [NoValidImages, EstimationRejected, EstimationTimeout];
    }

    // Values of the estimation.image.result tag.
    public static class ImageResults
    {
        public const string Analysed = "analysed";
        public const string DownloadError = "download_error";
        public const string AnalysisError = "analysis_error";
        public const string AnalysisTimeout = "analysis_timeout";

        public static readonly string[] All = [Analysed, DownloadError, AnalysisError, AnalysisTimeout];
    }
}
