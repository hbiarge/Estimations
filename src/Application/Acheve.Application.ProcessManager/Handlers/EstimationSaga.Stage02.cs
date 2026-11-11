using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public Task Handle(ImageAnalysisAccepted message)
        {
            logger.LogInformation(
                    "Case number {caseNumber}. Image analysis request sent for image {imageId}.",
                    message.CaseNumber,
                    message.ImageId);

            var currentImage = FindImage(message.CaseNumber, message.ImageId);

            // The result may already be here, or an earlier copy of this message already restarted the wait.
            if (currentImage is null || currentImage.Analyzed || currentImage.AnalysisAccepted)
            {
                return Task.CompletedTask;
            }

            // The wait so far was for the worker to send the image. The external service now has it,
            // so it gets the whole wait to answer. The timeout already running picks up the new count.
            currentImage.AnalysisAccepted = true;
            currentImage.CurrentAnalysisWaits = 0;

            return Task.CompletedTask;
        }

        public async Task Handle(ImageAnalysisTimeout message)
        {
            var currentImage = FindImage(message.CaseNumber, message.ImageId);

            if (currentImage is null)
            {
                return;
            }

            // Check if the image have been processed meanwhile
            if (currentImage.Analyzed)
            {
                return;
            }

            var elapsedWaitTime =
                TimeSpan.FromSeconds(
                    CaseImage.AnalysisWaitTime.TotalSeconds * (currentImage.CurrentAnalysisWaits + 1));

            if (currentImage.CurrentAnalysisWaits < CaseImage.MaxAnalysisWaits)
            {
                currentImage.CurrentAnalysisWaits += 1;

                logger.LogInformation(
                    "Case number {caseNumber}. Still waiting to receive the analysis of image {imageId}. ({currentAnalysisWaits}/{maxAnalysisWaits}) [{analysisWaitTime}]",
                    message.CaseNumber,
                    message.ImageId,
                    currentImage.CurrentAnalysisWaits,
                    CaseImage.MaxAnalysisWaits,
                    elapsedWaitTime);

                await bus.DeferLocal(
                    CaseImage.AnalysisWaitTime,
                    new ImageAnalysisTimeout
                    {
                        CaseNumber = message.CaseNumber,
                        ImageId = message.ImageId
                    });
            }
            else
            {
                logger.LogWarning(
                    "Case number {caseNumber}. Wait time exceeded to get the analysis of image {imageId}. [{analysisWaitTime}]",
                    message.CaseNumber,
                    message.ImageId,
                    elapsedWaitTime);

                // Treat the image as failed and continue with the rest of the case.
                currentImage.AnalysisError = CaseImage.WaitTimeExceeded;
                RecordWhenHandled(() => metrics.ImageFinished(ImageResults.AnalysisTimeout));

                await VerifyIfAllImagesAnalyzed(message.CaseNumber);
            }
        }

        public async Task Handle(ImageAnalysed message)
        {
            logger.LogInformation(
                "Case number {caseNumber}. Analysis received for image {imageId}.",
                message.CaseNumber,
                message.ImageId);

            var currentImage = FindImage(message.CaseNumber, message.ImageId);

            if (currentImage is null || currentImage.Analyzed)
            {
                if (currentImage is not null)
                {
                    LogIgnoredMessage(message.CaseNumber, message.ImageId, nameof(ImageAnalysed));
                }

                // A duplicate callback (or one after the wait ended) stored metadata nobody will use.
                await DeleteUnusedAttachment(message.MetadataTicket, currentImage?.AnalysisTicket);
                return;
            }

            currentImage.AnalysisTicket = message.MetadataTicket;
            RecordWhenHandled(() => metrics.ImageFinished(ImageResults.Analysed));

            await VerifyIfAllImagesAnalyzed(message.CaseNumber);
        }

        public async Task Handle(UnableToAnalyseImage message)
        {
            logger.LogWarning(
                "Case number {caseNumber}. Unable to get the analysis of image {imageId}. {imageAnalysisError}",
                message.CaseNumber,
                message.ImageId,
                message.Error);

            var currentImage = FindImage(message.CaseNumber, message.ImageId);

            if (currentImage is null)
            {
                return;
            }

            if (currentImage.Analyzed)
            {
                LogIgnoredMessage(message.CaseNumber, message.ImageId, nameof(UnableToAnalyseImage));
                return;
            }

            currentImage.AnalysisError = message.Error;
            RecordWhenHandled(() => metrics.ImageFinished(ImageResults.AnalysisError));

            await VerifyIfAllImagesAnalyzed(message.CaseNumber);
        }

        private async Task VerifyIfAllImagesAnalyzed(Guid caseNumber)
        {
            // The case moves to the estimation only once.
            if (Data.State >= EstimationStates.WaitingForEstimation)
            {
                return;
            }

            var allImagesProcessed = Data.Images.All(x => x.Analyzed);

            if (!allImagesProcessed)
            {
                logger.LogInformation(
                    "Case number {caseNumber}. Waiting for other images to be analysed...",
                    caseNumber);

                return;
            }

            logger.LogInformation(
                    "Case number {caseNumber}. All images analysed.",
                    caseNumber);

            // Do we have at least one image processed with analysis information?
            // If so, ask for an external estimation.
            // If not, the case fails and the client is notified.
            var imagesWithMetadata = Data.Images
                .Where(x => x.AvailableToEstimate)
                .ToArray();

            if (imagesWithMetadata.Length == 0)
            {
                await FailEstimation(
                    "There are no images with valid metadata to send to the external estimation system",
                    FailureReasons.NoValidImages);
                return;
            }

            await ChangeState(EstimationStates.WaitingForEstimation);

            await bus.Send(new EstimateCase
            {
                CaseNumber = Data.CaseNumber,
                Metadata = imagesWithMetadata.Select(image => new ImageMetadata
                {
                    ImageId = image.Id,
                    MetadataTicket = image.AnalysisTicket!
                }).ToArray()
            });

            // The wait starts with the command, so an estimation request that is never answered still ends.
            await bus.DeferLocal(
                EstimationState.EstimationWaitTime,
                new EstimationTimeout
                {
                    CaseNumber = Data.CaseNumber
                });
        }
    }
}
