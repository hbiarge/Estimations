using Acheve.Common.Messages;
using Rebus.Handlers;

namespace Acheve.Application.CallbackNotifier.Handlers
{
    public class NotifyEstimationFailureHandler(
        ClientNotifier notifier,
        ILogger<NotifyEstimationFailureHandler> logger) : IHandleMessages<NotifyEstimationFailure>
    {
        public Task Handle(NotifyEstimationFailure message)
        {
            logger.LogInformation(
                "Receiving case number {caseNumber} to notify estimation error (attempt {attempt}): {estimationError}.",
                message.CaseNumber,
                message.Attempt,
                message.Reason);

            return notifier.Notify(message.CaseNumber, message.CallbackUrl, message.Attempt, () =>
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
