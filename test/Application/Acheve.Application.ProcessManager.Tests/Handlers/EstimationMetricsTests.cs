using System.Diagnostics.Metrics;
using Acheve.Application.ProcessManager.Handlers;
using Acheve.Application.ProcessManager.Tests.Factories;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Time.Testing;
using Rebus.TestHelpers;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    // The case-level metrics the saga records for the Grafana dashboards.
    public sealed class EstimationMetricsTests : IDisposable
    {
        private readonly FakeBus _bus = new();
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        private readonly IMeterFactory _meterFactory = Sagas.CreateMeterFactory();
        private readonly EstimationMetrics _metrics;
        private readonly SagaFixture<EstimationSaga> _sut;
        private readonly Guid _caseNumber = Guid.NewGuid();

        private readonly MetricCollector<long> _started;
        private readonly MetricCollector<long> _finished;
        private readonly MetricCollector<double> _caseDuration;
        private readonly MetricCollector<double> _stateDuration;
        private readonly MetricCollector<long> _images;

        public EstimationMetricsTests()
        {
            // Created before the collectors, so they don't see the series it starts at 0.
            _metrics = new EstimationMetrics(_meterFactory);

            _started = Collector<long>("estimations.cases.started");
            _finished = Collector<long>("estimations.cases.finished");
            _caseDuration = Collector<double>("estimations.case.duration");
            _stateDuration = Collector<double>("estimations.case.state.duration");
            _images = Collector<long>("estimations.images.finished");

            _sut = SagaFixture.For(() => Sagas.Create(_bus, _metrics, _time));
        }

        public void Dispose()
        {
            _sut.Dispose();
            (_meterFactory as IDisposable)?.Dispose();
        }

        [Fact]
        public void Successful_case_records_its_result_and_durations()
        {
            StartCase(imageCount: 2);
            Wait(1);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageDownloaded(1));
            Wait(12);
            _sut.Deliver(ImageAnalysed(0));
            _sut.Deliver(ImageAnalysed(1));
            Wait(10);
            _sut.Deliver(new EstimationCompleted { CaseNumber = _caseNumber, EstimationTicket = "estimation" });
            Wait(2);
            _sut.Deliver(new NotificationCompleted { CaseNumber = _caseNumber, Attempt = 1 });

            _started.GetMeasurementSnapshot().ShouldHaveSingleItem().Value.ShouldBe(1);

            var finished = _finished.GetMeasurementSnapshot().ShouldHaveSingleItem();
            finished.Tags["estimation.result"].ShouldBe("success");
            finished.Tags["estimation.final_state"].ShouldBe(nameof(EstimationStates.ClientNotified));
            finished.Tags["estimation.failure_reason"].ShouldBe(FailureReasons.None);

            _caseDuration.GetMeasurementSnapshot().ShouldHaveSingleItem().Value.ShouldBe(25);

            StateDurations().ShouldBe(new Dictionary<string, double>
            {
                [nameof(EstimationStates.WaitingForImagesToBeDownloaded)] = 1,
                [nameof(EstimationStates.WaitingForImagesToBeAnalysed)] = 12,
                [nameof(EstimationStates.WaitingForEstimation)] = 10,
                [nameof(EstimationStates.EstimationReady)] = 2
            });

            RecordedImageResults().ShouldBe([ImageResults.Analysed, ImageResults.Analysed]);
        }

        [Fact]
        public void Case_without_usable_images_records_the_failure_reason_and_image_results()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(new UnableToDownloadImage { CaseNumber = _caseNumber, ImageId = 0, Error = "404" });
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(new UnableToAnalyseImage { CaseNumber = _caseNumber, ImageId = 1, Error = "500" });
            GiveUpNotifying();

            var finished = _finished.GetMeasurementSnapshot().ShouldHaveSingleItem();
            finished.Tags["estimation.result"].ShouldBe("failure");
            finished.Tags["estimation.final_state"].ShouldBe(nameof(EstimationStates.ClientNotificationError));
            finished.Tags["estimation.failure_reason"].ShouldBe(FailureReasons.NoValidImages);

            RecordedImageResults().ShouldBe(
                [ImageResults.DownloadError, ImageResults.AnalysisError],
                ignoreOrder: true);
        }

        [Fact]
        public void Estimation_wait_timeout_is_recorded_as_the_failure_reason()
        {
            StartCase(imageCount: 1);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageAnalysed(0));
            _sut.Deliver(new EstimationRequestAccepted { CaseNumber = _caseNumber });

            for (var i = 0; i <= EstimationState.MaxEstimationWaits; i++)
            {
                _sut.Deliver(new EstimationTimeout { CaseNumber = _caseNumber });
            }

            _sut.Deliver(new NotificationCompleted { CaseNumber = _caseNumber, Attempt = 1 });

            var finished = _finished.GetMeasurementSnapshot().ShouldHaveSingleItem();
            finished.Tags["estimation.result"].ShouldBe("failure");
            finished.Tags["estimation.final_state"].ShouldBe(nameof(EstimationStates.ClientNotified));
            finished.Tags["estimation.failure_reason"].ShouldBe(FailureReasons.EstimationTimeout);
        }

        [Fact]
        public void Download_timeout_is_recorded_as_an_image_result()
        {
            StartCase(imageCount: 1);

            _sut.Deliver(new ImageDownloadTimeout { CaseNumber = _caseNumber, ImageId = 0 });

            RecordedImageResults().ShouldBe([ImageResults.DownloadTimeout]);
        }

        [Fact]
        public void Image_analysis_timeout_is_recorded_as_an_image_result()
        {
            StartCase(imageCount: 1);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(new ImageAnalysisAccepted { CaseNumber = _caseNumber, ImageId = 0 });

            for (var i = 0; i <= CaseImage.MaxAnalysisWaits; i++)
            {
                _sut.Deliver(new ImageAnalysisTimeout { CaseNumber = _caseNumber, ImageId = 0 });
            }

            RecordedImageResults().ShouldBe([ImageResults.AnalysisTimeout]);
        }

        [Fact]
        public void Duplicate_and_late_messages_record_nothing_twice()
        {
            StartCase(imageCount: 1);
            _sut.Deliver(RequestEstimation(imageCount: 1));
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageAnalysed(0));
            _sut.Deliver(ImageAnalysed(0));
            _sut.Deliver(new UnableToAnalyseImage { CaseNumber = _caseNumber, ImageId = 0, Error = "late" });

            _started.GetMeasurementSnapshot().ShouldHaveSingleItem();
            RecordedImageResults().ShouldBe([ImageResults.Analysed]);
        }

        [Fact]
        public void Every_known_counter_series_starts_at_zero()
        {
            using var finished = Collector<long>("estimations.cases.finished");
            using var images = Collector<long>("estimations.images.finished");

            _ = new EstimationMetrics(_meterFactory);

            // 2 final states x (success + 3 failure reasons), and every image result.
            finished.GetMeasurementSnapshot().Count.ShouldBe(8);
            images.GetMeasurementSnapshot().Select(x => x.Tags["estimation.image.result"])
                .ShouldBe(ImageResults.All, ignoreOrder: true);
            finished.GetMeasurementSnapshot().ShouldAllBe(x => x.Value == 0);
        }

        [Fact]
        public void Handler_that_fails_and_is_retried_counts_the_case_once()
        {
            var failingTime = new FailingOnceTimeProvider(failOnCall: 2);
            using var sut = SagaFixture.For(() => Sagas.Create(_bus, _metrics, failingTime));

            // The first attempt registers the "case started" metric and then fails. The second delivery is the retry.
            sut.Deliver(RequestEstimation(imageCount: 1));
            sut.Deliver(RequestEstimation(imageCount: 1));

            sut.HandlerExceptions.ShouldHaveSingleItem();
            sut.Data.OfType<EstimationState>().ShouldHaveSingleItem();
            _started.GetMeasurementSnapshot().ShouldHaveSingleItem();
        }

        // Throws on one call to GetUtcNow, to make a handler fail halfway through.
        private sealed class FailingOnceTimeProvider(int failOnCall) : TimeProvider
        {
            private int _calls;

            public override DateTimeOffset GetUtcNow() =>
                ++_calls == failOnCall
                    ? throw new InvalidOperationException("Simulated failure")
                    : base.GetUtcNow();
        }

        private MetricCollector<T> Collector<T>(string instrument) where T : struct =>
            new(_meterFactory, Constants.Telemetry.MeterName, instrument);

        private Dictionary<string, double> StateDurations() =>
            _stateDuration.GetMeasurementSnapshot()
                .ToDictionary(x => (string)x.Tags["estimation.state"]!, x => x.Value);

        private List<string> RecordedImageResults() =>
            _images.GetMeasurementSnapshot().Select(x => (string)x.Tags["estimation.image.result"]!).ToList();

        // Every notification attempt fails until the saga gives up.
        private void GiveUpNotifying()
        {
            for (var attempt = 1; attempt <= EstimationState.MaxNotificationAttempts; attempt++)
            {
                _sut.Deliver(new UnableToNotify { CaseNumber = _caseNumber, Attempt = attempt, Error = "down" });
            }
        }

        private void Wait(int seconds) => _time.Advance(TimeSpan.FromSeconds(seconds));

        private void StartCase(int imageCount) => _sut.Deliver(RequestEstimation(imageCount));

        private EstimationRequested RequestEstimation(int imageCount) => new()
        {
            CaseNumber = _caseNumber,
            ClientId = "client",
            CallbackUrl = "http://notification.test",
            ImageUrls = Enumerable.Range(0, imageCount).Select(i => $"http://images.test/image{i}.png").ToArray()
        };

        private ImageDownloaded ImageDownloaded(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId, ImageTicket = $"image-{imageId}" };

        private ImageAnalysed ImageAnalysed(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId, MetadataTicket = $"metadata-{imageId}" };
    }
}
