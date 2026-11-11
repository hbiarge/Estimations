using Acheve.Application.ProcessManager.Handlers;
using Acheve.Application.ProcessManager.Tests.Factories;
using Acheve.Common.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Rebus.Sagas;
using Rebus.TestHelpers;
using Rebus.TestHelpers.Events;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    public sealed class EstimationSagaTests : IDisposable
    {
        private readonly FakeBus _bus;
        private readonly SagaFixture<EstimationSaga> _sut;
        
        private readonly Guid _caseNumber = Guid.NewGuid();

        public EstimationSagaTests()
        {
            _bus = new FakeBus();
            _sut = SagaFixture.For(() => Sagas.Create(_bus));
        }

        public void Dispose()
        {
            _sut.Dispose();
        }

        [Fact]
        public void A_new_request_creates_the_case_and_starts_the_downloads()
        {
            var requestEstimationMessage = Messages.RequestEstimationMessage(_caseNumber);
            var imagesCount = requestEstimationMessage.ImageUrls.Count;

            _sut.Created += data =>
            {
                var state = data.ShouldBeAssignableTo<EstimationState>();
                state.Id.ShouldNotBe(Guid.Empty);
                state.CaseNumber.ShouldBe(_caseNumber);
                state.ClientId.ShouldBe(requestEstimationMessage.ClientId);
                state.CallbackUrl.ShouldBe(requestEstimationMessage.CallbackUrl);
                state.Images.Count.ShouldBe(imagesCount);
            };

            _sut.Deliver(requestEstimationMessage);

            var estimationStateChangedMessages = _bus.Events.OfType<MessageSent<EstimationStateChanged>>();
            var stateChangeMessage = estimationStateChangedMessages.ShouldHaveSingleItem();
            stateChangeMessage.CommandMessage.State.ShouldBe(EstimationStates.WaitingForImagesToBeDownloaded);

            var imageUrlReceivedMessages = _bus.Events.OfType<MessageSent<DownloadImage>>();
            imageUrlReceivedMessages.Count().ShouldBe(imagesCount);
        }

        [Fact]
        public void A_downloaded_image_is_sent_to_be_analysed()
        {
            var requestEstimationMessage = Messages.RequestEstimationMessage(_caseNumber);
            var imageDownloadedMessage = Messages.ImageDownloadedMessage(_caseNumber, 0);

            _sut.Deliver(requestEstimationMessage);

            _sut.Updated += data =>
            {
                var state = data.ShouldBeAssignableTo<EstimationState>();
                var imageUpdated = state.Images.ElementAt(imageDownloadedMessage.ImageId);
                imageUpdated.ImageTicket.ShouldNotBeNullOrEmpty();
                imageUpdated.Downloaded.ShouldBeTrue();
            };

            _sut.Deliver(imageDownloadedMessage);

            var imageReadyMessages = _bus.Events.OfType<MessageSent<AnalyseImage>>();
            var imageReadyMessage = imageReadyMessages.ShouldHaveSingleItem();
            imageReadyMessage.CommandMessage.ImageTicket.ShouldBe(imageDownloadedMessage.ImageTicket);
        }
    }
}
