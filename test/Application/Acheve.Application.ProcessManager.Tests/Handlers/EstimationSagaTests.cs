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
            var logger = new NullLogger<EstimationSaga>();
            _sut = SagaFixture.For(() => new EstimationSaga(_bus, logger));
        }

        public void Dispose()
        {
            _sut.Dispose();
        }

        [Fact]
        public void Patata()
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

            var imageUrlReceivedMessages = _bus.Events.OfType<MessageSent<ImageUrlReceived>>();
            imageUrlReceivedMessages.Count().ShouldBe(imagesCount);
        }

        [Fact]
        public void Patata2()
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

            var imageReadyMessages = _bus.Events.OfType<MessageSent<ImageReady>>();
            var imageReadyMessage = imageReadyMessages.ShouldHaveSingleItem();
            imageReadyMessage.CommandMessage.ImageTicket.ShouldBe(imageDownloadedMessage.ImageTicket);
        }
    }
}
