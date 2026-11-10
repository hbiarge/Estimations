using Acheve.Application.ProcessManager.Handlers;
using Acheve.Application.ProcessManager.Tests.Factories;
using Acheve.Common.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Rebus.TestHelpers;
using Rebus.TestHelpers.Events;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    // Covers the timeouts and how the saga handles duplicate or late messages.
    public sealed class EstimationSagaFlowTests : IDisposable
    {
        private readonly FakeBus _bus = new();
        private readonly SagaFixture<EstimationSaga> _sut;
        private readonly Guid _caseNumber = Guid.NewGuid();

        public EstimationSagaFlowTests()
        {
            _sut = SagaFixture.For(() => Sagas.Create(_bus));
        }

        public void Dispose() => _sut.Dispose();

        [Fact]
        public void Duplicate_estimation_request_does_not_restart_the_case()
        {
            StartCase(imageCount: 2);
            _bus.Clear();

            _sut.Deliver(RequestEstimation(imageCount: 2));

            Sent<ImageUrlReceived>().ShouldBeEmpty();
            Sent<EstimationStateChanged>().ShouldBeEmpty();
        }

        [Fact]
        public void Duplicate_image_downloaded_sends_image_ready_once()
        {
            StartCase(imageCount: 2);

            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageDownloaded(0));

            Sent<ImageReady>().ShouldHaveSingleItem();
        }

        [Fact]
        public void Messages_for_an_unknown_image_are_ignored()
        {
            StartCase(imageCount: 1);
            _bus.Clear();

            _sut.Deliver(ImageDownloaded(7));
            _sut.Deliver(ImageAnalized(7));
            _sut.Deliver(AwaitImage(7));

            _bus.Events.OfType<MessageSent>().ShouldBeEmpty();
            _bus.Events.OfType<MessageDeferredToSelf>().ShouldBeEmpty();
        }

        [Fact]
        public void Image_ready_carries_the_extension_without_the_dot()
        {
            _sut.Deliver(new EstimationRequested
            {
                CaseNumber = _caseNumber,
                ClientId = "client",
                CallbackUrl = "http://notification.test",
                ImageUrls = ["http://images.test/photo.jpg?size=large"]
            });

            _sut.Deliver(ImageDownloaded(0));

            var imageReady = Sent<ImageReady>().ShouldHaveSingleItem();
            imageReady.ImageExtension.ShouldBe("jpg");
            imageReady.ImageName.ShouldBe($"{_caseNumber:D}_0.jpg");
        }

        [Fact]
        public void Reported_states_only_move_forward()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(ImageAnalized(0));
            _sut.Deliver(ImageAnalized(1));
            _sut.Deliver(EstimationCompleted());

            Sent<EstimationStateChanged>().Select(x => x.State).ShouldBe(
            [
                EstimationStates.WaitingForImagesToBeDownloaded,
                EstimationStates.WaitingForImagesToBeAnalysed,
                EstimationStates.WaitingForEstimation,
                EstimationStates.EstimationReady
            ]);
        }

        [Fact]
        public void Case_without_usable_images_fails_the_estimation()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(new UnableToDownloadImage { CaseNumber = _caseNumber, ImageId = 0, Error = "404" });
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(UnableToAnalize(1));

            Sent<ExternalEstimationReady>().ShouldBeEmpty();
            Sent<EstimationError>().ShouldHaveSingleItem();
            Sent<EstimationStateChanged>().Last().State.ShouldBe(EstimationStates.EstimationError);
            Data.State.ShouldBe(EstimationStates.EstimationError);
        }

        [Fact]
        public void Waiting_for_image_analysis_starts_the_timeout_once()
        {
            StartCase(imageCount: 1);
            _sut.Deliver(ImageDownloaded(0));

            _sut.Deliver(AwaitImage(0));
            _sut.Deliver(AwaitImage(0));

            var deferred = Deferred<StillAwaitingImageToBeProcessed>().ShouldHaveSingleItem();
            deferred.Delay.ShouldBe(CaseImage.AnalisysWaitTime);
            deferred.CommandMessage.ImageId.ShouldBe(0);
        }

        [Fact]
        public void Image_analysis_timeout_continues_with_the_other_images()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(ImageAnalized(0));
            _sut.Deliver(AwaitImage(1));

            for (var i = 0; i <= CaseImage.MaxAnalisysWaits; i++)
            {
                _sut.Deliver(StillAwaitingImage(1));
            }

            Deferred<StillAwaitingImageToBeProcessed>().Count.ShouldBe(CaseImage.MaxAnalisysWaits + 1);
            Data.Images.Single(x => x.Id == 1).AnalysisError.ShouldBe("Wait time exceeded");
            Data.State.ShouldBe(EstimationStates.WaitingForEstimation);

            var estimation = Sent<ExternalEstimationReady>().ShouldHaveSingleItem();
            estimation.Metadata.ShouldHaveSingleItem().ImageId.ShouldBe(0);
        }

        [Fact]
        public void Late_image_analysis_after_timeout_does_not_request_a_second_estimation()
        {
            StartCase(imageCount: 1);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(AwaitImage(0));

            for (var i = 0; i <= CaseImage.MaxAnalisysWaits; i++)
            {
                _sut.Deliver(StillAwaitingImage(0));
            }

            _sut.Deliver(ImageAnalized(0));

            Sent<ExternalEstimationReady>().ShouldBeEmpty();
            Sent<EstimationError>().ShouldHaveSingleItem();
        }

        [Fact]
        public void Duplicate_image_analysis_requests_the_estimation_once()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(ImageAnalized(0));
            _sut.Deliver(ImageAnalized(1));

            _sut.Deliver(ImageAnalized(1));
            _sut.Deliver(UnableToAnalize(0));

            Sent<ExternalEstimationReady>().ShouldHaveSingleItem().Metadata.Count.ShouldBe(2);
        }

        [Fact]
        public void Waiting_for_estimation_starts_the_timeout_once()
        {
            StartCaseWaitingForEstimation();

            _sut.Deliver(new AwaitEstimationToBeProcessed { CaseNumber = _caseNumber });
            _sut.Deliver(new AwaitEstimationToBeProcessed { CaseNumber = _caseNumber });

            Deferred<StillAwaitingEstimationToBeProcessed>().ShouldHaveSingleItem()
                .Delay.ShouldBe(EstimationState.EstimationWaitTime);
        }

        [Fact]
        public void Estimation_timeout_after_the_estimation_arrived_does_nothing()
        {
            StartCaseWaitingForEstimation();
            _sut.Deliver(new AwaitEstimationToBeProcessed { CaseNumber = _caseNumber });
            _sut.Deliver(EstimationCompleted());
            _bus.Clear();

            _sut.Deliver(new StillAwaitingEstimationToBeProcessed { CaseNumber = _caseNumber });

            _bus.Events.OfType<MessageSent>().ShouldBeEmpty();
            _bus.Events.OfType<MessageDeferredToSelf>().ShouldBeEmpty();
            Data.State.ShouldBe(EstimationStates.EstimationReady);
        }

        [Fact]
        public void Estimation_timeout_notifies_an_error_to_the_client()
        {
            StartCaseWaitingForEstimation();
            _sut.Deliver(new AwaitEstimationToBeProcessed { CaseNumber = _caseNumber });

            for (var i = 0; i <= EstimationState.MaxEstimationWaits; i++)
            {
                _sut.Deliver(new StillAwaitingEstimationToBeProcessed { CaseNumber = _caseNumber });
            }

            Sent<EstimationError>().ShouldHaveSingleItem().Reason.ShouldBe("Wait time exceeded");
            Sent<UnableToEstimate>().ShouldBeEmpty();
            Data.State.ShouldBe(EstimationStates.EstimationError);
        }

        [Fact]
        public void Late_estimation_after_timeout_is_ignored()
        {
            StartCaseWaitingForEstimation();
            _sut.Deliver(new AwaitEstimationToBeProcessed { CaseNumber = _caseNumber });

            for (var i = 0; i <= EstimationState.MaxEstimationWaits; i++)
            {
                _sut.Deliver(new StillAwaitingEstimationToBeProcessed { CaseNumber = _caseNumber });
            }

            _sut.Deliver(EstimationCompleted());

            Sent<EstimationReady>().ShouldBeEmpty();
            Data.State.ShouldBe(EstimationStates.EstimationError);
        }

        [Fact]
        public void Duplicate_estimation_notifies_the_client_once()
        {
            StartCaseWaitingForEstimation();

            _sut.Deliver(EstimationCompleted());
            _sut.Deliver(EstimationCompleted());

            Sent<EstimationReady>().ShouldHaveSingleItem();
        }

        private EstimationState Data => _sut.Data.OfType<EstimationState>().Single();

        private List<T> Sent<T>() =>
            _bus.Events.OfType<MessageSent<T>>().Select(x => x.CommandMessage).ToList();

        private List<MessageDeferredToSelf<T>> Deferred<T>() =>
            _bus.Events.OfType<MessageDeferredToSelf<T>>().ToList();

        private void StartCase(int imageCount) => _sut.Deliver(RequestEstimation(imageCount));

        private void StartCaseWaitingForEstimation()
        {
            StartCase(imageCount: 1);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageAnalized(0));
            Sent<ExternalEstimationReady>().ShouldHaveSingleItem();
        }

        private EstimationRequested RequestEstimation(int imageCount) => new()
        {
            CaseNumber = _caseNumber,
            ClientId = "client",
            CallbackUrl = "http://notification.test",
            ImageUrls = Enumerable.Range(0, imageCount).Select(i => $"http://images.test/image{i}.png").ToArray()
        };

        private ImageDownloaded ImageDownloaded(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId, ImageTicket = $"image-{imageId}" };

        private AwaitImageToBeProcessed AwaitImage(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId };

        private StillAwaitingImageToBeProcessed StillAwaitingImage(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId };

        private ImageAnalized ImageAnalized(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId, MetadataTicket = $"metadata-{imageId}" };

        private UnableToAnalizeImage UnableToAnalize(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId, Error = "error" };

        private EstimationCompleted EstimationCompleted() =>
            new() { CaseNumber = _caseNumber, EstimationTicket = "estimation" };
    }
}
