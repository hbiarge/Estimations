using Grpc.Core;
using StateHolder;

namespace Acheve.Application.StateHolder.Services
{
    public class StateService(State state) : StateHolderService.StateHolderServiceBase
    {
        public override Task<StateResponse> StateQuery(StateRequest request, ServerCallContext context)
        {
            var currentState = state.GetState(request.Ticket, request.ClientId);

            return Task.FromResult(new StateResponse
            {
                Ticket = request.Ticket,
                State = currentState
            });
        }
    }
}
