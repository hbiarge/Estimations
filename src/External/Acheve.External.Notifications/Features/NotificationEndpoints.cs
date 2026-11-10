using Acheve.Common.Shared;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Acheve.External.Notifications.Features
{
    public static class NotificationEndpoints
    {
        public static IEndpointRouteBuilder MapNotification(this IEndpointRouteBuilder app)
        {
            app.MapPost("/Notification", Notification);

            return app;
        }

        private static async Task<NoContent> Notification(
            NotificationModel model,
            HttpRequest httpRequest,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger(typeof(NotificationEndpoints));

            RandomFailureGenerator.RandomFail(
                failThreshold: Constants.FailureThresholds.ExternalNotificationsProcessing, // Exception is thrown if a random value between 0 and 1 is greater or equal this value
                url: httpRequest.GetDisplayUrl(),
                logger: logger);

            // Simulate some time to process
            await Task.Delay(2000);

            logger.LogInformation("Received case completion notification. CaseNumber: {caseNumber} with result {result}", model.CaseNumber, model.Result);

            return TypedResults.NoContent();
        }
    }
}
