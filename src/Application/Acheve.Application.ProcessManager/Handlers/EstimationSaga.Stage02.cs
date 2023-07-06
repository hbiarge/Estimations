using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public Task Handle(AwaitImageToBeProcessed message)
        {
            _logger.LogInformation(
                    "Case number {caseNumber}. Image analysis request sent for image {imageId}.",
                    message.CaseNumber,
                    message.ImageId);

            return Task.CompletedTask;
        }

        public async Task Handle(StillAwaitingImageToBeProcessed message)
        {
            var currentImage = Data.Images.Single(x => x.Id == message.ImageId);

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

                _logger.LogInformation(
                    "Case number {caseNumber}. Still waiting to receive the analisys of image {imageId}. ({currentAnalisysWaits}/{maxAnalisysWaits}) [{analisysWaitTime}]",
                    message.CaseNumber,
                    message.ImageId,
                    currentImage.CurrentAnalisysWaits,
                    CaseImage.MaxAnalisysWaits,
                    elapsedWaitTime);

                await _bus.DeferLocal(
                    CaseImage.AnalisysWaitTime,
                    new StillAwaitingImageToBeProcessed
                    {
                        CaseNumber = message.CaseNumber,
                        ImageId = message.ImageId
                    });
            }
            else
            {
                _logger.LogWarning(
                    "Case number {caseNumber}. Wait time exceeded to get the analisys of image {imageId}. [{analisysWaitTime}]",
                    message.CaseNumber,
                    message.ImageId,
                    elapsedWaitTime);

                Data.State = EstimationStates.StuckWaitingForImagesToBeAnalysed;

                await _bus.Send(new EstimationStateChanged
                {
                    CaseNumber = Data.CaseNumber,
                    ClientId = Data.ClientId,
                    State = Data.State
                });

                await _bus.Send(new UnableToAnalizeImage
                {
                    CaseNumber = Data.CaseNumber,
                    ImageId = message.ImageId,
                    Error = "Wait time exceeded"
                });
            }
        }

        public async Task Handle(ImageAnalized message)
        {
            _logger.LogInformation(
                "Case number {caseNumber}. Analisys received for image {imageId}.",
                message.CaseNumber,
                message.ImageId);

            var currentImage = Data.Images.Single(x => x.Id == message.ImageId);
            currentImage.AnalysisTicket = message.MetadataTicket;

            await VerifyIfAllImagesAnalyzed(message.CaseNumber);
        }

        public async Task Handle(UnableToAnalizeImage message)
        {
            _logger.LogWarning(
                "Case number {caseNumber}. Unable to get the analisys of image {imageId}. {imageAnalisysError}",
                message.CaseNumber,
                message.ImageId,
                message.Error);

            var currentImage = Data.Images.Single(x => x.Id == message.ImageId);
            currentImage.AnalysisError = message.Error;

            await VerifyIfAllImagesAnalyzed(message.CaseNumber);
        }

        private async Task VerifyIfAllImagesAnalyzed(Guid caseNumber)
        {
            var allImagesProcessed = Data.Images.All(x => x.Analyzed);

            if (!allImagesProcessed)
            {
                _logger.LogInformation(
                    "Case number {caseNumber}. Waiting for other images to be analyzed...",
                    caseNumber);

                return;
            }

            _logger.LogInformation(
                    "Case number {caseNumber}. All images analyzed.",
                    caseNumber);

            Data.State = EstimationStates.ImagesAnalysed;

            await _bus.Send(new EstimationStateChanged
            {
                CaseNumber = Data.CaseNumber,
                ClientId = Data.ClientId,
                State = Data.State
            });

            // Do we have at least one image processed with analisys information?
            // If so, send the allImagesProcessed to get an external estimation
            // If not, just send a notification error
            var imagesWithMetadata = Data.Images
                .Where(x => x.AvailableToEstimate)
                .ToArray();

            if (imagesWithMetadata.Length > 0)
            {
                await _bus.Send(new ExternalEstimationReady
                {
                    CaseNumber = Data.CaseNumber,
                    Metadata = imagesWithMetadata.Select(image => new ImageMetadata
                    {
                        ImageId = image.Id,
                        Metadata = image.AnalysisTicket!
                    }).ToArray()
                });
            }
            else
            {
                await _bus.Send(new EstimationError
                {
                    CaseNumber = Data.CaseNumber,
                    CallbackUrl = Data.CallbackUrl,
                    Reason = "There are no images with valid metadata to send to the external estimation system"
                });
            }
        }
    }
}
