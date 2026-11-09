using Acheve.Common.Messages;
using Rebus.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public async Task Handle(ImageDownloaded message)
        {
            _logger.LogInformation(
                "Case number {caseNumber}. Image {imageId} downloaded successfully.",
                message.CaseNumber,
                message.ImageId);

            var currentImage = FindImage(message.CaseNumber, message.ImageId);

            if (currentImage is null)
            {
                return;
            }

            if (currentImage.Downloaded)
            {
                LogIgnoredMessage(message.CaseNumber, message.ImageId, nameof(ImageDownloaded));
                return;
            }

            currentImage.ImageTicket = message.ImageTicket;

            await _bus.Send(new ImageReady
            {
                CaseNumber = Data.CaseNumber,
                ImageId = currentImage.Id,
                ImageTicket = currentImage.ImageTicket,
                ImageExtension = currentImage.Extension,
            });

            await VerifyIfAllImagesDownloaded(message.CaseNumber);
        }

        public async Task Handle(UnableToDownloadImage message)
        {
            _logger.LogWarning(
                "Case number {caseNumber}. Unable to download image {imageId}. {imageDownloadError}",
                message.CaseNumber,
                message.ImageId,
                message.Error);

            var currentImage = FindImage(message.CaseNumber, message.ImageId);

            if (currentImage is null)
            {
                return;
            }

            if (currentImage.Downloaded)
            {
                LogIgnoredMessage(message.CaseNumber, message.ImageId, nameof(UnableToDownloadImage));
                return;
            }

            // At this point we have tried at least 3 times to download
            // the image with retries at the httpClient level
            // We can also reschedule the message to be processed later
            //await _bus.Defer(TimeSpan.FromSeconds(30), new ImageUrlReceived
            //{
            //    CaseNumber = message.CaseNumber,
            //    ImageId = message.ImageId,
            //    ImageUrl = currentImage.Url
            //});

            currentImage.DownloadError = message.Error;

            await VerifyIfAllImagesDownloaded(message.CaseNumber);

            // An image that can't be downloaded also counts as analyzed (it is never sent to analyze).
            // If it was the last image pending, the case can move on to the estimation.
            await VerifyIfAllImagesAnalyzed(message.CaseNumber);
        }

        // Duplicate or late messages (redeliveries, retried callbacks, results after a timeout)
        // must not repeat a step that already happened.
        private void LogIgnoredMessage(Guid caseNumber, int imageId, string messageType)
        {
            _logger.LogWarning(
                "Case number {caseNumber}. Image {imageId} already has a result. Ignoring {messageType}.",
                caseNumber,
                imageId,
                messageType);
        }

        private async Task VerifyIfAllImagesDownloaded(Guid caseNumber)
        {
            var allImagesDownloaded = Data.Images.All(x => x.Downloaded);

            if (!allImagesDownloaded)
            {
                _logger.LogInformation(
                    "Case number {caseNumber}. Waiting for other images to be downloaded...",
                    caseNumber);

                return;
            }

            _logger.LogInformation(
                    "Case number {caseNumber}. All images downloaded.",
                    caseNumber);

            Data.State = EstimationStates.WaitingForImagesToBeAnalysed;

            await _bus.Send(new EstimationStateChanged
            {
                CaseNumber = Data.CaseNumber,
                ClientId = Data.ClientId,
                State = Data.State
            });
        }
    }
}
