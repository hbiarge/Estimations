using Grpc.Core;
using StateHolder;

namespace Acheve.Application.StateHolder.Services
{
    public class StateService(CaseLookup cases) : StateHolderService.StateHolderServiceBase
    {
        public override async Task<StateResponse> StateQuery(StateRequest request, ServerCallContext context)
        {
            var snapshot = await cases.Find(request.Ticket, request.CustomerId, context.CancellationToken);

            var response = new StateResponse
            {
                Ticket = request.Ticket,
                Found = snapshot is not null
            };

            if (snapshot is not null)
            {
                response.State = snapshot.State.ToString("G");
                response.Result = snapshot.Result ?? string.Empty;
                response.FailureReason = snapshot.FailureReason ?? string.Empty;
                response.EstimationTicket = snapshot.EstimationTicket ?? string.Empty;
                response.SkippedImages.AddRange(snapshot.SkippedImages.Select(x => new SkippedImage
                {
                    ImageId = x.ImageId,
                    Url = x.Url,
                    Reason = x.Reason
                }));
            }

            return response;
        }
    }
}
