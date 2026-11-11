using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public async Task Handle(ImageDownloaded message)
        {
            logger.LogInformation(
                "Case number {caseNumber}. Image {imageId} downloaded successfully.",
                message.CaseNumber,
                message.ImageId);

            var currentImage = FindImage(message.CaseNumber, message.ImageId);

            if (currentImage is null || currentImage.Downloaded)
            {
                if (currentImage is not null)
                {
                    LogIgnoredMessage(message.CaseNumber, message.ImageId, nameof(ImageDownloaded));
                }

                // A second download (or one that arrived after the wait ended) stored a copy nobody will use.
                await DeleteUnusedAttachment(message.ImageTicket, currentImage?.ImageTicket);
                return;
            }

            currentImage.ImageTicket = message.ImageTicket;

            await bus.Send(new AnalyseImage
            {
                CaseNumber = Data.CaseNumber,
                ImageId = currentImage.Id,
                ImageTicket = currentImage.ImageTicket,
                ImageExtension = currentImage.Extension,
            });

            // The wait starts with the command, so an analysis request that is never answered still ends.
            await bus.DeferLocal(
                CaseImage.AnalysisWaitTime,
                new ImageAnalysisTimeout
                {
                    CaseNumber = Data.CaseNumber,
                    ImageId = currentImage.Id
                });

            await VerifyIfAllImagesDownloaded(message.CaseNumber);
        }

        public async Task Handle(UnableToDownloadImage message)
        {
            logger.LogWarning(
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

            await FailDownload(currentImage, message.Error, ImageResults.DownloadError);
        }

        public async Task Handle(ImageDownloadTimeout message)
        {
            var currentImage = FindImage(message.CaseNumber, message.ImageId);

            // The download result arrived meanwhile.
            if (currentImage is null || currentImage.Downloaded)
            {
                return;
            }

            logger.LogWarning(
                "Case number {caseNumber}. Wait time exceeded to download image {imageId}. [{downloadWaitTime}]",
                message.CaseNumber,
                message.ImageId,
                CaseImage.DownloadWaitTime);

            await FailDownload(currentImage, CaseImage.WaitTimeExceeded, ImageResults.DownloadTimeout);
        }

        private async Task FailDownload(CaseImage image, string error, string imageResult)
        {
            image.DownloadError = error;
            RecordWhenHandled(() => metrics.ImageFinished(imageResult));

            await VerifyIfAllImagesDownloaded(Data.CaseNumber);

            // An image that can't be downloaded also counts as analysed (it is never sent to analyse).
            // If it was the last image pending, the case can move on to the estimation.
            await VerifyIfAllImagesAnalyzed(Data.CaseNumber);
        }

        // Duplicate or late messages (redeliveries, retried callbacks, results after a timeout)
        // must not repeat a step that already happened.
        private void LogIgnoredMessage(Guid caseNumber, int imageId, string messageType)
        {
            logger.LogWarning(
                "Case number {caseNumber}. Image {imageId} already has a result. Ignoring {messageType}.",
                caseNumber,
                imageId,
                messageType);
        }

        // Deletes the attachment of a message the saga ignored, unless it is the one the case kept.
        private async Task DeleteUnusedAttachment(string? ticket, string? keptTicket)
        {
            if (string.IsNullOrEmpty(ticket) || ticket == keptTicket)
            {
                return;
            }

            await attachments.Delete(ticket);
        }

        private async Task VerifyIfAllImagesDownloaded(Guid caseNumber)
        {
            var allImagesDownloaded = Data.Images.All(x => x.Downloaded);

            if (!allImagesDownloaded)
            {
                logger.LogInformation(
                    "Case number {caseNumber}. Waiting for other images to be downloaded...",
                    caseNumber);

                return;
            }

            logger.LogInformation(
                    "Case number {caseNumber}. All images downloaded.",
                    caseNumber);

            await ChangeState(EstimationStates.WaitingForImagesToBeAnalysed);
        }
    }
}
