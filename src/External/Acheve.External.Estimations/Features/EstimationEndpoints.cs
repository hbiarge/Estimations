using System.Net.Mime;
using System.Text;
using Acheve.Common.Chaos;
using Acheve.External.Shared;
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
            IHttpClientFactory httpClientFactory,
            ChaosState chaos,
            IBackgroundTaskQueue queue,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger(typeof(EstimationEndpoints));

            logger.LogInformation(
                "Received new estimation request. Case number: {caseNumber}; Callback: {callback}",
                model.CaseNumber,
                model.CallbackUrl);

            await queue.QueueBackgroundWorkItemAsync(
                _ => NotifyBack(model.CaseNumber, model.CallbackUrl, httpClientFactory, chaos.PlanCallback(), logger, CancellationToken.None));

            logger.LogInformation(
                "Estimation request queued. Case number: {caseNumber}",
                model.CaseNumber);

            return TypedResults.NoContent();
        }

        private static async ValueTask NotifyBack(
            string caseNumber,
            string callbackUrl,
            IHttpClientFactory httpClientFactory,
            CallbackPlan chaos,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            // Simulate some time to process, plus any delay the chaos adds
            await Task.Delay(TimeSpan.FromSeconds(10) + chaos.Delay, cancellationToken);

            if (chaos.Drop)
            {
                logger.LogWarning("Chaos: the estimation is never notified back. Case number: {caseNumber}", caseNumber);
                return;
            }

            var client = httpClientFactory.CreateClient("EstimationConfirmation");

            var contentString = System.Text.Json.JsonSerializer.Serialize(new
            {
                Estimation = $"This is the estimation for the case {caseNumber}"
            });

            // Twice when the chaos duplicates the callback.
            for (var attempt = 0; attempt < (chaos.Duplicate ? 2 : 1); attempt++)
            {
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
}
