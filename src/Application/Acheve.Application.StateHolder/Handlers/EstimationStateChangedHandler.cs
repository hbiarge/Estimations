using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Handlers;

namespace Acheve.Application.StateHolder.Handlers
{
    public class EstimationStateChangedHandler(
        State state,
        ICaseStateStore store,
        ILogger<EstimationStateChangedHandler> logger) : IHandleMessages<EstimationStateChanged>
    {
        public async Task Handle(EstimationStateChanged message)
        {
            using var customerScope = CustomerTelemetry.BeginScope(logger, message.CustomerId);

            logger.LogInformation(
                "Case number {caseNumber} state change. New state: {state}",
                message.CaseNumber,
                message.State);

            var snapshot = state.AddOrUpdateState(message);

            // Always saved, even when memory didn't change: if an earlier try of this message failed here,
            // memory already has the change and the store doesn't. The store ignores what it already has.
            await store.Save(snapshot, CancellationToken.None);
        }
    }
}
