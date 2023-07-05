using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public async Task Handle(ImageDownloaded message)
        {
            _logger.LogInformation(
                "Case number {caseNumber}. Image {imageId} downloaded successfully. Try to analyse image...",
                message.CaseNumber,
                message.ImageId);

            var currentImage = Data.Images.Single(x => x.Id == message.ImageId);
            currentImage.ImageTicket = message.ImageTicket;

            await _bus.Send(new ImageReady
            {
                CaseNumber = Data.CaseNumber,
                ImageId = currentImage.Id,
                ImageTicket = currentImage.ImageTicket
            });

            await VerifyIfAllImagesDownloaded();
        }

        public async Task Handle(UnableToDownloadImage message)
        {
            _logger.LogWarning(
                "Case number {caseNumber}. Unable to download image {imageId}. {imageDownloadError}",
                message.CaseNumber,
                message.ImageId,
                message.Error);

            var currentImage = Data.Images.Single(x => x.Id == message.ImageId);

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

            await VerifyIfAllImagesDownloaded();

            // We should verify also if all images are processed
            // because in case we can not download the last image
            // all images are processed and we can proceed to estimate
            // TODO: Verify if this is needed
            await VerifyIfAllImagesProcessed();
        }

        private async Task VerifyIfAllImagesDownloaded()
        {
            var allImagesDownloaded = Data.Images.All(x => x.Downloaded);

            if (!allImagesDownloaded)
            {
                return;
            }

            Data.State = EstimationStates.ImagesDownloaded;

            await _bus.Send(new EstimationStateChanged
            {
                CaseNumber = Data.CaseNumber,
                ClientId = Data.ClientId,
                State = Data.State
            });
        }
    }
}
