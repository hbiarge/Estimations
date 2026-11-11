using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        // True only while the case waits for the estimation: not before it was asked, not after it has a result.
        private bool WaitingForEstimation => Data.State == EstimationStates.WaitingForEstimation;

        public Task Handle(EstimationRequestAccepted message)
        {
            logger.LogInformation(
                    "Case number {caseNumber}. External estimation request sent.",
                    message.CaseNumber);

            // The result may already be here, or an earlier copy of this message already restarted the wait.
            if (!WaitingForEstimation || Data.EstimationAccepted)
            {
                return Task.CompletedTask;
            }

            // The wait so far was for the worker to send the request. The external service now has it,
            // so it gets the whole wait to answer. The timeout already running picks up the new count.
            Data.EstimationAccepted = true;
            Data.CurrentExternalEstimationWaits = 0;

            return Task.CompletedTask;
        }

        public async Task Handle(EstimationTimeout message)
        {
            // Check if the estimation (or an estimation error) have been received meanwhile
            if (!WaitingForEstimation)
            {
                return;
            }

            var elapsedWaitTime =
                TimeSpan.FromSeconds(
                    EstimationState.EstimationWaitTime.TotalSeconds * (Data.CurrentExternalEstimationWaits + 1));

            if (Data.CurrentExternalEstimationWaits < EstimationState.MaxEstimationWaits)
            {
                Data.CurrentExternalEstimationWaits += 1;

                logger.LogInformation(
                    "Case number {caseNumber}. Still waiting to receive external estimation. ({currentEstimationWaits}/{maxEstimationWaits}) [{estimationsWaitTime}]",
                    message.CaseNumber,
                    Data.CurrentExternalEstimationWaits,
                    EstimationState.MaxEstimationWaits,
                    elapsedWaitTime);

                await bus.DeferLocal(
                    EstimationState.EstimationWaitTime,
                    new EstimationTimeout
                    {
                        CaseNumber = message.CaseNumber
                    });
            }
            else
            {
                logger.LogWarning(
                    "Case number {caseNumber}. Wait time exceeded to get the external estimation. [{estimationsWaitTime}]",
                    message.CaseNumber,
                    elapsedWaitTime);

                await FailEstimation(CaseImage.WaitTimeExceeded, FailureReasons.EstimationTimeout);
            }
        }

        public async Task Handle(EstimationCompleted message)
        {
            logger.LogInformation(
                "Case number {caseNumber}. External estimation received.",
                message.CaseNumber);

            if (!WaitingForEstimation)
            {
                LogIgnoredEstimationMessage(message.CaseNumber, nameof(EstimationCompleted));

                // A duplicate callback (or one after the wait ended) stored an estimation nobody will use.
                await DeleteUnusedAttachment(message.EstimationTicket, Data.ExternalEstimationTicket);
                return;
            }

            Data.ExternalEstimationTicket = message.EstimationTicket;

            await ChangeState(EstimationStates.EstimationReady);

            await StartNotification();
        }

        public async Task Handle(UnableToEstimate message)
        {
            logger.LogWarning(
                "Case number {caseNumber}. Unable to get the external estimation. {estimationError}",
                message.CaseNumber,
                message.Error);

            if (!WaitingForEstimation)
            {
                LogIgnoredEstimationMessage(message.CaseNumber, nameof(UnableToEstimate));
                return;
            }

            await FailEstimation(message.Error, FailureReasons.EstimationRejected);
        }

        private async Task FailEstimation(string error, string failureReason)
        {
            Data.ExternalEstimationError = error;
            Data.FailureReason = failureReason;

            await ChangeState(EstimationStates.EstimationError);

            await StartNotification();
        }

        private void LogIgnoredEstimationMessage(Guid caseNumber, string messageType)
        {
            logger.LogWarning(
                "Case number {caseNumber}. The case is not waiting for an estimation. Ignoring {messageType}.",
                caseNumber,
                messageType);
        }
    }
}
