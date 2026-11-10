using Acheve.Common.Messages;
using Rebus.Handlers;

namespace Acheve.Application.StateHolder.Handlers
{
    public class EstimationStateChangedHandler(
        State state,
        ILogger<EstimationStateChangedHandler> logger) : IHandleMessages<EstimationStateChanged>
    {
        public Task Handle(EstimationStateChanged message)
        {
            logger.LogInformation(
                "Case number {caseNumber} state change. New state: {state}",
                message.CaseNumber,
                message.State);

            state.AddOrUpdateState(
                message.CaseNumber.ToString("D"), 
                message.ClientId, 
                message.State);

            return Task.CompletedTask;
        }
    }
}
