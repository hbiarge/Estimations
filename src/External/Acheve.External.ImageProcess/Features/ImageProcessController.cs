using System.Net.Mime;
using System.Reflection;
using System.Text;
using Acheve.Common.Shared;
using Acheve.External.Shared;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Acheve.External.ImageProcess.Features
{
    [ApiController]
    [Route("[controller]")]
    public class ImageProcessController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ImageProcessController> _logger;
        private readonly IBackgroundTaskQueue _queue;

        public ImageProcessController(
            IHttpClientFactory httpClientFactory, 
            ILogger<ImageProcessController> logger,
            IBackgroundTaskQueue queue)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _queue = queue;
        }

        [HttpPost]
        public async Task<IActionResult> ProcessImage([FromForm]ImageProcessModel model)
        {
            if(model.CaseNumber is null || model.CallbackUrl is null || model.ImageId is null)
            {
                return BadRequest();
            }

            RandomFailureGenerator.RandomFail(
                failThreshold: Constants.FailureThresholds.ExternalImagesProcessing, // Exception is thrown if a random value between 0 and 1 is greater or equal this value
                url: Request.GetDisplayUrl(), 
                logger: _logger);

            _logger.LogInformation(
                "Received a request to analyze a new image. Image {imageId} of case number: {caseNumber}; Callback: {callback}",
                model.ImageId,
                model.CaseNumber,                
                model.CallbackUrl);

            await _queue.QueueBackgroundWorkItemAsync(
                _ => NotifyBack(
                    model.CaseNumber, 
                    model.CallbackUrl,
                    model.ImageId,
                    CancellationToken.None));

            _logger.LogInformation(
               "Image analisys queued. Image {imageId} of case number: {caseNumber}",
               model.ImageId,
               model.CaseNumber);

            return NoContent();
        }

        private async ValueTask NotifyBack(
            string caseNumber,
            string callbackUrl,
            string imageId,
            CancellationToken cancellationToken)
        {
            // Simulate some time to process
            await Task.Delay(10_000, cancellationToken);

            var client = _httpClientFactory.CreateClient("ImageProcessConfirmation");

            var contentString = System.Text.Json.JsonSerializer.Serialize(new
            {
                Metadata = $"This is the metadata for the image {imageId}. Case number {caseNumber} "
            });

            await client.PostAsync(
                new Uri(callbackUrl),
                new StringContent(contentString, Encoding.UTF8, MediaTypeNames.Application.Json),
                cancellationToken);

            _logger.LogInformation(
                "Image analisys notified back. Image {imageId} of case number: {caseNumber}",
                imageId,
                caseNumber);
        }
    }
}
