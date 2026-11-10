using Acheve.Common.Messages;
using Rebus.Bus;
using Rebus.Handlers;

namespace Acheve.Application.ImageDownloads.Handlers
{
    public class ImageUrlReceivedHandler(
        IBus bus,
        IHttpClientFactory httpClientFactory,
        ILogger<ImageUrlReceivedHandler> logger) : IHandleMessages<ImageUrlReceived>
    {
        public async Task Handle(ImageUrlReceived message)
        {
            logger.LogInformation(
                "New request to download image: case number {caseNumber}. ImageId: {imageId}, ImageUrl {url}",
                message.CaseNumber,
                message.ImageId,
                message.ImageUrl);

            var client = httpClientFactory.CreateClient("images");

            try
            {
                var stream = await client.GetStreamAsync(message.ImageUrl);

                logger.LogInformation(
                    "Download successfully for image {imageId} for case number {caseNumber}.",
                    message.ImageId,
                    message.CaseNumber);

                var attachment = await bus.Advanced.DataBus.CreateAttachment(stream);

                await bus.Send(new ImageDownloaded
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId,
                    ImageTicket = attachment.Id
                });
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    "Download error for image {imageId} for case number {caseNumber}. {downloadError}",
                    message.ImageId,
                    message.CaseNumber,
                    e.Message);

                await bus.Send(new UnableToDownloadImage
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId,
                    Error = e.Message
                });
            }
        }
    }
}
