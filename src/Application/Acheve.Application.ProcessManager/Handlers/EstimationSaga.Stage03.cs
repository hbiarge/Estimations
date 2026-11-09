using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public async Task Handle(AwaitEstimationToBeProcessed message)
        {
            _logger.LogInformation(
                    "Case number {caseNumber}. External estimation request sent.",
                    message.CaseNumber);

            // The result may already be here, or the wait may have started with an earlier copy of this message.
            if (Data.EstimationFinished || Data.EstimationWaitStarted)
            {
                return;
            }

            Data.EstimationWaitStarted = true;

            // Start the timeout: if the external service never calls back, we stop waiting for the estimation.
            await _bus.DeferLocal(
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

                _logger.LogInformation(
                    "Case number {caseNumber}. Still waiting to receive external estimation. ({currentEstimationWaits}/{maxEstimationWaits}) [{estimationsWaitTime}]",
                    message.CaseNumber,
                    Data.CurrentExternalEstimationWaits,
                    EstimationState.MaxEstimationWaits,
                    elapsedWaitTime);

                await _bus.DeferLocal(
                    EstimationState.EstimationWaitTime,
                    new StillAwaitingEstimationToBeProcessed
                    {
                        CaseNumber = message.CaseNumber
                    });
            }
            else
            {
                _logger.LogWarning(
                    "Case number {caseNumber}. Wait time exceeded to get the external estimation. [{estimationsWaitTime}]",
                    message.CaseNumber,
                    elapsedWaitTime);

                await FailEstimation("Wait time exceeded");
            }
        }

        public async Task Handle(EstimationCompleted message)
        {
            _logger.LogInformation(
                "Case number {caseNumber}. External estimation received.",
                message.CaseNumber);

            if (Data.EstimationFinished)
            {
                LogIgnoredEstimationMessage(message.CaseNumber, nameof(EstimationCompleted));
                return;
            }

            Data.ExternalEstimationTicket = message.EstimationTicket;
            Data.State = EstimationStates.EstimationReady;

            await _bus.Send(new EstimationStateChanged
            {
                CaseNumber = Data.CaseNumber,
                ClientId = Data.ClientId,
                State = Data.State
            });

            await _bus.Send(new EstimationReady
            {
                CaseNumber = Data.CaseNumber,
                CallbackUrl = Data.CallbackUrl,
                EstimationId = Data.ExternalEstimationTicket
            });
        }

        public async Task Handle(UnableToEstimate message)
        {
            _logger.LogWarning(
                "Case number {caseNumber}. Unable to get the external estimation. {estimationError}",
                message.CaseNumber,
                message.Error);

            if (Data.EstimationFinished)
            {
                LogIgnoredEstimationMessage(message.CaseNumber, nameof(UnableToEstimate));
                return;
            }

            await FailEstimation(message.Error);
        }

        private async Task FailEstimation(string error)
        {
            Data.ExternalEstimationError = error;
            Data.State = EstimationStates.EstimationError;

            await _bus.Send(new EstimationStateChanged
            {
                CaseNumber = Data.CaseNumber,
                ClientId = Data.ClientId,
                State = Data.State
            });

            await _bus.Send(new EstimationError
            {
                CaseNumber = Data.CaseNumber,
                CallbackUrl = Data.CallbackUrl,
                Reason = error
            });
        }

        private void LogIgnoredEstimationMessage(Guid caseNumber, string messageType)
        {
            _logger.LogWarning(
                "Case number {caseNumber}. The estimation already has a result. Ignoring {messageType}.",
                caseNumber,
                messageType);
        }
    }
}
