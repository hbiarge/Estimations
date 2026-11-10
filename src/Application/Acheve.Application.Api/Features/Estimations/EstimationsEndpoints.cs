using System.Diagnostics;
using System.Security.Claims;
using Acheve.Common.Messages;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.HttpResults;
using Rebus.Bus;
using StateHolder;

namespace Acheve.Application.Api.Features.Estimations
{
    public static class EstimationsEndpoints
    {
        public static IEndpointRouteBuilder MapEstimations(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/Estimations")
                .RequireAuthorization(policy => policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser());

            group.MapGet("/{ticket:guid}", GetEstimationState);
            group.MapPost("/", ProcessNewEstimation);

            return app;
        }

        private static async Task<Ok<EstimationState>> GetEstimationState(
            Guid ticket,
            ClaimsPrincipal user,
            StateHolderService.StateHolderServiceClient stateHolderService)
        {
            var clientId = user.Identity?.Name ?? "N/A";

            var stateResponse = await stateHolderService.StateQueryAsync(
                new StateRequest
                {
                    Ticket = ticket.ToString("D"),
                    ClientId = clientId
                });

            return TypedResults.Ok(new EstimationState
            {
                Ticket = stateResponse.Ticket,
                State = stateResponse.State
            });
        }

        private static async Task<Accepted<NewEstimationResponse>> ProcessNewEstimation(
            NewEstimationRequest request,
            ClaimsPrincipal user,
            IBus bus)
        {
            var currentActivity = Activity.Current;
            var clientId = user.Identity?.Name ?? "N/A";

            var message = new EstimationRequested
            {
                CaseNumber = Guid.NewGuid(),
                ClientId = clientId,
                CallbackUrl = request.CallBackUri,
                ImageUrls = request.ImageUrls
            };

            await bus.Send(message);

            var token = message.CaseNumber.ToString("D");

            return TypedResults.Accepted(
                $"/Estimations/{token}",
                new NewEstimationResponse
                {
                    Token = token,
                    OperationId = currentActivity?.RootId ?? "N/A"
                });
        }
    }
}
