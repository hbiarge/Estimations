using Grpc.Core;
using StateHolder;

namespace Acheve.Application.StateHolder.Services
{
    public class StateService : StateHolderService.StateHolderServiceBase
    {
        private readonly State _state;
        private readonly ILogger<StateService> _logger;

        public StateService(State state, ILogger<StateService> logger)
        {
            _state = state;
            _logger = logger;
        }

        public override Task<StateResponse> StateQuery(StateRequest request, ServerCallContext context)
        {
            var currentState = _state.GetState(request.Ticket, request.ClientId);

            return Task.FromResult(new StateResponse
            {
                Ticket = request.Ticket,
                State = currentState
            });
        }
    }
}
