using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public async Task Handle(AwaitImageToBeProcessed message)
        {
            logger.LogInformation(
                    "Case number {caseNumber}. Image analysis request sent for image {imageId}.",
                    message.CaseNumber,
                    message.ImageId);

            var currentImage = FindImage(message.CaseNumber, message.ImageId);

            if (currentImage is null)
            {
                return;
            }

            // The result may already be here, or the wait may have started with an earlier copy of this message.
            if (currentImage.Analyzed || currentImage.AnalysisWaitStarted)
            {
                return;
            }

            currentImage.AnalysisWaitStarted = true;

            // Start the timeout: if the external service never calls back, we stop waiting for this image.
            await bus.DeferLocal(
                CaseImage.AnalisysWaitTime,
                new StillAwaitingImageToBeProcessed
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId
                });
        }

        public async Task Handle(StillAwaitingImageToBeProcessed message)
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
                    CaseImage.AnalisysWaitTime.TotalSeconds * (currentImage.CurrentAnalisysWaits + 1));

            if (currentImage.CurrentAnalisysWaits < CaseImage.MaxAnalisysWaits)
            {
                currentImage.CurrentAnalisysWaits += 1;

                logger.LogInformation(
                    "Case number {caseNumber}. Still waiting to receive the analisys of image {imageId}. ({currentAnalisysWaits}/{maxAnalisysWaits}) [{analisysWaitTime}]",
                    message.CaseNumber,
                    message.ImageId,
                    currentImage.CurrentAnalisysWaits,
                    CaseImage.MaxAnalisysWaits,
                    elapsedWaitTime);

                await bus.DeferLocal(
                    CaseImage.AnalisysWaitTime,
                    new StillAwaitingImageToBeProcessed
                    {
                        CaseNumber = message.CaseNumber,
                        ImageId = message.ImageId
                    });
            }
            else
            {
                logger.LogWarning(
                    "Case number {caseNumber}. Wait time exceeded to get the analisys of image {imageId}. [{analisysWaitTime}]",
                    message.CaseNumber,
                    message.ImageId,
                    elapsedWaitTime);

                // Treat the image as failed and continue with the rest of the case.
                currentImage.AnalysisError = "Wait time exceeded";
                RecordWhenHandled(() => metrics.ImageFinished(ImageResults.AnalysisTimeout));

                await VerifyIfAllImagesAnalyzed(message.CaseNumber);
            }
        }

        public async Task Handle(ImageAnalized message)
        {
            logger.LogInformation(
                "Case number {caseNumber}. Analisys received for image {imageId}.",
                message.CaseNumber,
                message.ImageId);

            var currentImage = FindImage(message.CaseNumber, message.ImageId);

            if (currentImage is null)
            {
                return;
            }

            if (currentImage.Analyzed)
            {
                LogIgnoredMessage(message.CaseNumber, message.ImageId, nameof(ImageAnalized));
                return;
            }

            currentImage.AnalysisTicket = message.MetadataTicket;
            RecordWhenHandled(() => metrics.ImageFinished(ImageResults.Analysed));

            await VerifyIfAllImagesAnalyzed(message.CaseNumber);
        }

        public async Task Handle(UnableToAnalizeImage message)
        {
            logger.LogWarning(
                "Case number {caseNumber}. Unable to get the analisys of image {imageId}. {imageAnalisysError}",
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
                LogIgnoredMessage(message.CaseNumber, message.ImageId, nameof(UnableToAnalizeImage));
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
                    "Case number {caseNumber}. Waiting for other images to be analyzed...",
                    caseNumber);

                return;
            }

            logger.LogInformation(
                    "Case number {caseNumber}. All images analyzed.",
                    caseNumber);

            // Do we have at least one image processed with analisys information?
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

            await bus.Send(new ExternalEstimationReady
            {
                CaseNumber = Data.CaseNumber,
                Metadata = imagesWithMetadata.Select(image => new ImageMetadata
                {
                    ImageId = image.Id,
                    Metadata = image.AnalysisTicket!
                }).ToArray()
            });
        }
    }
}
