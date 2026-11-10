using System.Net.Mime;
using System.Text;
using Acheve.Common.Shared;
using Acheve.External.Shared;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Acheve.External.Estimations.Features
{
    public static class EstimationEndpoints
    {
        public static IEndpointRouteBuilder MapEstimation(this IEndpointRouteBuilder app)
        {
            app.MapPost("/Estimation", ProcessEstimation);

            return app;
        }

        private static async Task<NoContent> ProcessEstimation(
            EstimationModel model,
            HttpRequest httpRequest,
            IHttpClientFactory httpClientFactory,
            IBackgroundTaskQueue queue,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger(typeof(EstimationEndpoints));

            RandomFailureGenerator.RandomFail(
                failThreshold: Constants.FailureThresholds.ExternalEstimationsProcessing, // Exception is thrown if a random value between 0 and 1 is greater or equal this value
                url: httpRequest.GetDisplayUrl(),
                logger: logger);

            logger.LogInformation(
                "Received new estimation request. Case number: {caseNumber}; Callback: {callback}",
                model.CaseNumber,
                model.CallbackUrl);

            await queue.QueueBackgroundWorkItemAsync(
                _ => NotifyBack(model.CaseNumber, model.CallbackUrl, httpClientFactory, logger, CancellationToken.None));

            logger.LogInformation(
                "Estimation request queued. Case number: {caseNumber}",
                model.CaseNumber);

            return TypedResults.NoContent();
        }

        private static async ValueTask NotifyBack(
            string caseNumber,
            string callbackUrl,
            IHttpClientFactory httpClientFactory,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // Simulate some time to process
            await Task.Delay(10_000, cancellationToken);

            var client = httpClientFactory.CreateClient("EstimationConfirmation");

            var contentString = System.Text.Json.JsonSerializer.Serialize(new
            {
                Estimation = $"This is the estimation for the case {caseNumber}"
            });

            await client.PostAsync(
                new Uri(callbackUrl),
                new StringContent(contentString, Encoding.UTF8, MediaTypeNames.Application.Json),
                cancellationToken);

            logger.LogInformation(
                "Estimation notified back. Case number: {caseNumber}",
                caseNumber);
        }
    }
}
