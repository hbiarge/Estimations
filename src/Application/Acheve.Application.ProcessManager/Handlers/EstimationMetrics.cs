using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using Acheve.Common.Messages;
using Acheve.Common.Shared;

namespace Acheve.Application.ProcessManager.Handlers
{
    // Case-level metrics recorded by the saga. Grafana's Overview and Case flow dashboards are built on them.
    public sealed class EstimationMetrics
    {
        // A case takes about 25 s, and several minutes when it waits for timeouts and notification retries.
        private static readonly double[] DurationBuckets = [1, 2, 5, 10, 15, 20, 25, 30, 45, 60, 90, 120, 180, 240, 300, 600, 900];

        private const string CustomerTag = CustomerTelemetry.Tag;

        private readonly CustomerLabels _customers;
        private readonly Counter<long> _casesStarted;
        private readonly Counter<long> _casesFinished;
        private readonly Histogram<double> _caseDuration;
        private readonly Histogram<double> _stateDuration;
        private readonly Counter<long> _imagesFinished;

        // The cases carry a customer label, but only a bounded one (see CustomerLabels): a customer outside the
        // Customers:Known setting is recorded as "other", so any number of customers adds no series.
        public EstimationMetrics(IMeterFactory meterFactory, CustomerLabels customers)
        {
            _customers = customers;

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
            foreach (var customer in _customers.All)
            {
                _casesStarted.Add(0, new KeyValuePair<string, object?>(CustomerTag, customer));
            }

            foreach (var result in ImageResults.All)
            {
                _imagesFinished.Add(0, ImageTags(result));
            }

            foreach (var customer in _customers.All)
            {
                foreach (var finalState in new[] { EstimationStates.CustomerNotified, EstimationStates.CustomerNotificationError })
                {
                    _casesFinished.Add(0, FinishedTags(customer, CaseResult.Success, finalState, FailureReasons.None));

                    foreach (var reason in FailureReasons.Failures)
                    {
                        _casesFinished.Add(0, FinishedTags(customer, CaseResult.Failure, finalState, reason));
                    }
                }
            }
        }

        public void CaseStarted(string customerId) =>
            _casesStarted.Add(1, new KeyValuePair<string, object?>(CustomerTag, _customers.For(customerId)));

        public void CaseFinished(
            string customerId, CaseResult result, EstimationStates finalState, string failureReason, TimeSpan? duration)
        {
            var customer = _customers.For(customerId);

            _casesFinished.Add(1, FinishedTags(customer, result, finalState, failureReason));

            // Cases started before the saga kept its start time have no duration.
            // The histogram has a series per bucket, so it only gets the result and the customer.
            if (duration is not null)
            {
                _caseDuration.Record(duration.Value.TotalSeconds, new TagList
                {
                    { "estimation.result", ResultTag(result) },
                    { CustomerTag, customer }
                });
            }
        }

        private static string ResultTag(CaseResult result) => result == CaseResult.Success ? "success" : "failure";

        private static TagList FinishedTags(string customer, CaseResult result, EstimationStates finalState, string failureReason) => new()
        {
            { "estimation.result", ResultTag(result) },
            { "estimation.final_state", finalState.ToString() },
            { "estimation.failure_reason", failureReason },
            { CustomerTag, customer }
        };

        public void StateLeft(EstimationStates state, TimeSpan duration) =>
            _stateDuration.Record(duration.TotalSeconds, new TagList
            {
                { EstimationStateTelemetry.OrderTag, state.OrderValue() },
                { EstimationStateTelemetry.StateTag, state.ToString() }
            });

        public void ImageFinished(string result) =>
            _imagesFinished.Add(1, ImageTags(result));

        // The order tag sorts before estimation.image.result as a Prometheus label, so a "by result" query
        // comes back in pipeline order (see EstimationStateTelemetry.OrderTag).
        private static TagList ImageTags(string result) => new()
        {
            { EstimationStateTelemetry.OrderTag, ImageResults.OrderValue(result) },
            { "estimation.image.result", result }
        };
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
        public const string DownloadTimeout = "download_timeout";
        public const string AnalysisError = "analysis_error";
        public const string AnalysisTimeout = "analysis_timeout";

        // In pipeline order: download problems first, then analysis problems, then success.
        public static readonly string[] All = [DownloadError, DownloadTimeout, AnalysisError, AnalysisTimeout, Analysed];

        public static string OrderValue(string result) =>
            ((Array.IndexOf(All, result) + 1) * 10).ToString("D2", CultureInfo.InvariantCulture);
    }
}
