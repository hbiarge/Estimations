using System.Net.Mime;
using System.Text;
using Acheve.Common.Chaos;
using Acheve.Common.Shared;
using Acheve.External.Shared;
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
            [FromHeader(Name = Idempotency.HeaderName)] string? idempotencyKey,
            SeenRequests seenRequests,
            IHttpClientFactory httpClientFactory,
            ChaosState chaos,
            IBackgroundTaskQueue queue,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger(typeof(ImageProcessEndpoints));

            logger.LogInformation(
                "Received a request to analyze a new image. Image {imageId} of case number: {caseNumber}; Callback: {callback}",
                model.ImageId,
                model.CaseNumber,
                model.CallbackUrl);

            // The same request sent again is accepted, but the image is analysed (and notified back) only once.
            if (!seenRequests.IsNew(idempotencyKey))
            {
                logger.LogInformation(
                    "Image {imageId} of case number {caseNumber} was already accepted. Not analysing it again.",
                    model.ImageId,
                    model.CaseNumber);

                return TypedResults.NoContent();
            }

            await queue.QueueBackgroundWorkItemAsync(
                _ => NotifyBack(
                    model.CaseNumber,
                    model.CallbackUrl,
                    model.ImageId,
                    httpClientFactory,
                    chaos.PlanCallback(),
                    logger,
                    CancellationToken.None));

            logger.LogInformation(
               "Image analysis queued. Image {imageId} of case number: {caseNumber}",
               model.ImageId,
               model.CaseNumber);

            return TypedResults.NoContent();
        }

        private static async ValueTask NotifyBack(
            string caseNumber,
            string callbackUrl,
            string imageId,
            IHttpClientFactory httpClientFactory,
            CallbackPlan chaos,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // Simulate some time to process, plus any delay the chaos adds
            await Task.Delay(TimeSpan.FromSeconds(10) + chaos.Delay, cancellationToken);

            if (chaos.Drop)
            {
                logger.LogWarning(
                    "Chaos: the image analysis is never notified back. Image {imageId} of case number: {caseNumber}",
                    imageId,
                    caseNumber);
                return;
            }

            var client = httpClientFactory.CreateClient("ImageProcessConfirmation");

            var contentString = System.Text.Json.JsonSerializer.Serialize(new
            {
                Metadata = $"This is the metadata for the image {imageId}. Case number {caseNumber} "
            });

            // Twice when the chaos duplicates the callback.
            for (var attempt = 0; attempt < (chaos.Duplicate ? 2 : 1); attempt++)
            {
                await client.PostAsync(
                    new Uri(callbackUrl),
                    new StringContent(contentString, Encoding.UTF8, MediaTypeNames.Application.Json),
                    cancellationToken);

                logger.LogInformation(
                    "Image analysis notified back. Image {imageId} of case number: {caseNumber}",
                    imageId,
                    caseNumber);
            }
        }
    }
}
