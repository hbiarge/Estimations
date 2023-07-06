using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        public Task Handle(AwaitEstimationToBeProcessed message)
        {
            _logger.LogInformation(
                    "Case number {caseNumber}. External estimation request sent.",
                    message.CaseNumber);

            return Task.CompletedTask;
        }

        public async Task Handle(StillAwaitingEstimationToBeProcessed message)
        {
            // Check if the estimation have been received meanwhile
            if (Data.State > EstimationStates.EstimationReady)
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

                await _bus.Send(new UnableToEstimate
                {
                    CaseNumber = Data.CaseNumber,
                    Error = "Wait time exceeded"
                });
            }
        }

        public async Task Handle(EstimationCompleted message)
        {
            _logger.LogInformation(
                "Case number {caseNumber}. External estimation received.",
                message.CaseNumber);

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

            Data.ExternalEstimationError = message.Error;
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
                Reason = message.Error
            });
        }
    }
}
