using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Handlers;

namespace Acheve.Application.CallbackNotifier.Handlers
{
    public class NotifyEstimationFailureHandler(
        CustomerNotifier notifier,
        ILogger<NotifyEstimationFailureHandler> logger) : IHandleMessages<NotifyEstimationFailure>
    {
        public async Task Handle(NotifyEstimationFailure message)
        {
            using var customerScope = CustomerTelemetry.BeginScope(logger, message.CustomerId);

            logger.LogInformation(
                "Receiving case number {caseNumber} to notify estimation error (attempt {attempt}): {estimationError}.",
                message.CaseNumber,
                message.Attempt,
                message.Reason);

            await notifier.Notify(message.CaseNumber, message.CustomerId, message.CallbackUrl, message.Attempt, () =>
                Task.FromResult<object>(new
                {
                    message.CaseNumber,
                    Result = CaseResults.Failure,
                    FailureReason = message.Reason,
                    message.SkippedImages
                }));
        }
    }
}
