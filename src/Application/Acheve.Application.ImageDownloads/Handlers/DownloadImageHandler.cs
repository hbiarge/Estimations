using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Bus;
using Rebus.Handlers;

namespace Acheve.Application.ImageDownloads.Handlers
{
    public class DownloadImageHandler(
        IBus bus,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<DownloadImageHandler> logger) : IHandleMessages<DownloadImage>
    {
        // Limits for one image: a server that sends too much or too slowly must not fill the disk or hold a worker.
        private readonly long _maxBytes = configuration.GetValue("Downloads:MaxBytes", 20L * 1024 * 1024);
        private readonly TimeSpan _timeout = configuration.GetValue("Downloads:Timeout", TimeSpan.FromSeconds(60));

        public async Task Handle(DownloadImage message)
        {
            logger.LogInformation(
                "New request to download image: case number {caseNumber}. ImageId: {imageId}, ImageUrl {url}",
                message.CaseNumber,
                message.ImageId,
                message.ImageUrl);

            try
            {
                var ticket = await Download(message.ImageUrl);

                logger.LogInformation(
                    "Download successfully for image {imageId} for case number {caseNumber}.",
                    message.ImageId,
                    message.CaseNumber);

                await bus.Send(new ImageDownloaded
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId,
                    ImageTicket = ticket
                });
            }
            catch (Exception e)
            {
                var error = e is OperationCanceledException
                    ? $"The download took longer than {_timeout.TotalSeconds:F0} s."
                    : e.Message;

                logger.LogWarning(
                    "Download error for image {imageId} for case number {caseNumber}. {downloadError}",
                    message.ImageId,
                    message.CaseNumber,
                    error);

                await bus.Send(new UnableToDownloadImage
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId,
                    Error = error
                });
            }
        }

        // Stores the image on the data bus and returns its ticket.
        private async Task<string> Download(string imageUrl)
        {
            if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException("The image URL is not an absolute http or https URL.");
            }

            var client = httpClientFactory.CreateClient("images");

            // One deadline for the whole download. The HTTP resilience timeouts end when the headers arrive,
            // so without it nothing would limit how long the body takes.
            using var deadline = new CancellationTokenSource(_timeout);

            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > _maxBytes)
            {
                throw new InvalidOperationException(
                    $"The image has {response.Content.Headers.ContentLength} bytes; the limit is {_maxBytes}.");
            }

            // The length header can be missing or wrong, so the copy is limited too.
            await using var body = new BoundedReadStream(
                await response.Content.ReadAsStreamAsync(deadline.Token), _maxBytes, deadline.Token);

            var attachment = await bus.Advanced.DataBus.CreateAttachment(body);

            return attachment.Id;
        }
    }
}
