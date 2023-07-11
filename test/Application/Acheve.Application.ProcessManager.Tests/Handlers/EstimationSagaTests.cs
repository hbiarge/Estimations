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
                var type = data.Should().BeAssignableTo<EstimationState>();
                type.Subject.Id.Should().NotBeEmpty();
                type.Subject.CaseNumber.Should().Be(_caseNumber);
                type.Subject.ClientId.Should().Be(requestEstimationMessage.ClientId);
                type.Subject.CallbackUrl.Should().Be(requestEstimationMessage.CallbackUrl);
                type.Subject.Images.Should().HaveCount(imagesCount);
            };

            _sut.Deliver(requestEstimationMessage);

            var estimationStateChangedMessages = _bus.Events.OfType<MessageSent<EstimationStateChanged>>();
            var stateChangeMessage = estimationStateChangedMessages.Should().ContainSingle();
            stateChangeMessage.Subject.CommandMessage.State.Should().Be(EstimationStates.WaitingForImagesToBeDownloaded);

            var imageUrlReceivedMessages = _bus.Events.OfType<MessageSent<ImageUrlReceived>>();
            imageUrlReceivedMessages.Should().HaveCount(imagesCount);
        }

        [Fact]
        public void Patata2()
        {
            var requestEstimationMessage = Messages.RequestEstimationMessage(_caseNumber);
            var imageDownloadedMessage = Messages.ImageDownloadedMessage(_caseNumber, 0);

            _sut.Deliver(requestEstimationMessage);

            _sut.Updated += data =>
            {
                var type = data.Should().BeAssignableTo<EstimationState>();
                var imageUpdated = type.Subject.Images.ElementAt(imageDownloadedMessage.ImageId);
                imageUpdated.ImageTicket.Should().NotBeEmpty();
                imageUpdated.Downloaded.Should().BeTrue();
            };

            _sut.Deliver(imageDownloadedMessage);

            var imageReadyMessages = _bus.Events.OfType<MessageSent<ImageReady>>();
            var imageReadyMessage = imageReadyMessages.Should().ContainSingle();
            imageReadyMessage.Subject.CommandMessage.ImageTicket.Should().Be(imageDownloadedMessage.ImageTicket);
        }
    }
}
