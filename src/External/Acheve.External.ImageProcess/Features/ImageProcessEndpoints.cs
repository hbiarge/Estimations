using System.Net.Mime;
using System.Text;
using Acheve.Common.Shared;
using Acheve.External.Shared;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Acheve.External.ImageProcess.Features
{
    public static class ImageProcessEndpoints
    {
        public static IEndpointRouteBuilder MapImageProcess(this IEndpointRouteBuilder app)
        {
            app.MapPost("/ImageProcess", ProcessImage)
                // Called service to service with a multipart form, not from a browser form.
                .DisableAntiforgery();

            return app;
        }

        private static async Task<NoContent> ProcessImage(
            [FromForm] ImageProcessModel model,
            HttpRequest httpRequest,
            IHttpClientFactory httpClientFactory,
            IBackgroundTaskQueue queue,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger(typeof(ImageProcessEndpoints));

            RandomFailureGenerator.RandomFail(
                failThreshold: Constants.FailureThresholds.ExternalImagesProcessing, // Exception is thrown if a random value between 0 and 1 is greater or equal this value
                url: httpRequest.GetDisplayUrl(),
                logger: logger);

            logger.LogInformation(
                "Received a request to analyze a new image. Image {imageId} of case number: {caseNumber}; Callback: {callback}",
                model.ImageId,
                model.CaseNumber,
                model.CallbackUrl);

            await queue.QueueBackgroundWorkItemAsync(
                _ => NotifyBack(
                    model.CaseNumber,
                    model.CallbackUrl,
                    model.ImageId,
                    httpClientFactory,
                    logger,
                    CancellationToken.None));

            logger.LogInformation(
               "Image analisys queued. Image {imageId} of case number: {caseNumber}",
               model.ImageId,
               model.CaseNumber);

            return TypedResults.NoContent();
        }

        private static async ValueTask NotifyBack(
            string caseNumber,
            string callbackUrl,
            string imageId,
            IHttpClientFactory httpClientFactory,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // Simulate some time to process
            await Task.Delay(10_000, cancellationToken);

            var client = httpClientFactory.CreateClient("ImageProcessConfirmation");

            var contentString = System.Text.Json.JsonSerializer.Serialize(new
            {
                Metadata = $"This is the metadata for the image {imageId}. Case number {caseNumber} "
            });

            await client.PostAsync(
                new Uri(callbackUrl),
                new StringContent(contentString, Encoding.UTF8, MediaTypeNames.Application.Json),
                cancellationToken);

            logger.LogInformation(
                "Image analisys notified back. Image {imageId} of case number: {caseNumber}",
                imageId,
                caseNumber);
        }
    }
}
