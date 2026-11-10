using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public async Task Handle(AwaitEstimationToBeProcessed message)
        {
            logger.LogInformation(
                    "Case number {caseNumber}. External estimation request sent.",
                    message.CaseNumber);

            // The result may already be here, or the wait may have started with an earlier copy of this message.
            if (Data.EstimationFinished || Data.EstimationWaitStarted)
            {
                return;
            }

            Data.EstimationWaitStarted = true;

            // Start the timeout: if the external service never calls back, we stop waiting for the estimation.
            await bus.DeferLocal(
                EstimationState.EstimationWaitTime,
                new StillAwaitingEstimationToBeProcessed
                {
                    CaseNumber = message.CaseNumber
                });
        }

        public async Task Handle(StillAwaitingEstimationToBeProcessed message)
        {
            // Check if the estimation (or an estimation error) have been received meanwhile
            if (Data.EstimationFinished)
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
                    new StillAwaitingEstimationToBeProcessed
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

                await FailEstimation("Wait time exceeded", FailureReasons.EstimationTimeout);
            }
        }

        public async Task Handle(EstimationCompleted message)
        {
            logger.LogInformation(
                "Case number {caseNumber}. External estimation received.",
                message.CaseNumber);

            if (Data.EstimationFinished)
            {
                LogIgnoredEstimationMessage(message.CaseNumber, nameof(EstimationCompleted));
                return;
            }

            Data.ExternalEstimationTicket = message.EstimationTicket;

            await ChangeState(EstimationStates.EstimationReady);

            await bus.Send(new EstimationReady
            {
                CaseNumber = Data.CaseNumber,
                CallbackUrl = Data.CallbackUrl,
                EstimationId = Data.ExternalEstimationTicket
            });
        }

        public async Task Handle(UnableToEstimate message)
        {
            logger.LogWarning(
                "Case number {caseNumber}. Unable to get the external estimation. {estimationError}",
                message.CaseNumber,
                message.Error);

            if (Data.EstimationFinished)
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

            await bus.Send(new EstimationError
            {
                CaseNumber = Data.CaseNumber,
                CallbackUrl = Data.CallbackUrl,
                Reason = error
            });
        }

        private void LogIgnoredEstimationMessage(Guid caseNumber, string messageType)
        {
            logger.LogWarning(
                "Case number {caseNumber}. The estimation already has a result. Ignoring {messageType}.",
                caseNumber,
                messageType);
        }
    }
}
