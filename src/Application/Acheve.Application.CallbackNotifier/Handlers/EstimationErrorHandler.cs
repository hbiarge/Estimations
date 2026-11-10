using System.Net.Mime;
using System.Text;
using Acheve.Common.Messages;
using Rebus.Bus;
using Rebus.Handlers;

namespace Acheve.Application.CallbackNotifier.Handlers
{
    public class EstimationErrorHandler(
        IBus bus,
        IHttpClientFactory httpClientFactory,
        ILogger<EstimationErrorHandler> logger) : IHandleMessages<EstimationError>
    {
        public async Task Handle(EstimationError message)
        {
            logger.LogInformation(
                "Receiving case number {caseNumber} to notify estimation error: {estimationError}.",
                message.CaseNumber,
                message.Reason);

            var client = httpClientFactory.CreateClient("notifications");
            var contentString = System.Text.Json.JsonSerializer.Serialize(new
            {
                message.CaseNumber,
                Result = "Failure",
                FailureReason = message.Reason
            });

            try
            {
                using var response = await client.PostAsync(
                    message.CallbackUrl,
                    new StringContent(contentString, Encoding.UTF8, MediaTypeNames.Application.Json));

                // An error response from the client is a failed notification, not a completed one.
                response.EnsureSuccessStatusCode();

                logger.LogInformation(
                    "Case {caseNumber} notified successfully.",
                    message.CaseNumber);

                await bus.Send(new NotificationCompleted
                {
                    CaseNumber = message.CaseNumber
                });
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    "Case number {caseNumber} notification error. {notificationError}",
                    message.CaseNumber,
                    e.Message);

                await bus.Send(new UnableToNotify
                {
                    CaseNumber = message.CaseNumber
                });
            }
        }
    }
}