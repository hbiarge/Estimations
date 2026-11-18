using System.Diagnostics;
using System.Security.Claims;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Rebus.Bus;
using StateHolder;

namespace Acheve.Application.Api.Features.Estimations
{
    public static class EstimationsEndpoints
    {
        public static IEndpointRouteBuilder MapEstimations(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/estimations")
                .RequireAuthorization(policy => policy
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                    .RequireAuthenticatedUser()
                    // A token without a customer would put its cases under no one: refuse it (403).
                    .RequireAssertion(context => CustomerClaims.CustomerIdOf(context.User) is not null));

            group.MapGet("/{ticket:guid}", GetEstimationState);
            group.MapPost("/", ProcessNewEstimation);

            return app;
        }

        // 404 when the customer has no case with this ticket. Right after POST /estimations answers 202 the case
        // may not be known yet for a moment, because it starts asynchronously.
        private static async Task<Results<Ok<EstimationState>, NotFound>> GetEstimationState(
            Guid ticket,
            ClaimsPrincipal user,
            StateHolderService.StateHolderServiceClient stateHolderService,
            IBus bus,
            ILoggerFactory loggerFactory)
        {
            var customerId = CustomerClaims.CustomerIdOf(user)!;

            using var customerScope = CustomerTelemetry.BeginScope(
                loggerFactory.CreateLogger(typeof(EstimationsEndpoints)), customerId);

            var stateResponse = await stateHolderService.StateQueryAsync(
                new StateRequest
                {
                    Ticket = ticket.ToString("D"),
                    CustomerId = customerId
                });

            if (!stateResponse.Found)
            {
                return TypedResults.NotFound();
            }

            return TypedResults.Ok(new EstimationState
            {
                Ticket = stateResponse.Ticket,
                State = stateResponse.State,
                Result = NullIfEmpty(stateResponse.Result),
                FailureReason = NullIfEmpty(stateResponse.FailureReason),
                Estimation = await ReadEstimation(stateResponse.EstimationTicket, bus, loggerFactory),
                SkippedImages = stateResponse.SkippedImages
                    .Select(x => new SkippedImageState { ImageId = x.ImageId, Url = x.Url, Reason = x.Reason })
                    .ToArray()
            });
        }

        // Null when the case has no estimation, or when it was deleted after its retention time.
        private static async Task<string?> ReadEstimation(string estimationTicket, IBus bus, ILoggerFactory loggerFactory)
        {
            if (string.IsNullOrEmpty(estimationTicket))
            {
                return null;
            }

            try
            {
                using var reader = new StreamReader(await bus.Advanced.DataBus.OpenRead(estimationTicket));

                return await reader.ReadToEndAsync();
            }
            catch (Exception e)
            {
                loggerFactory.CreateLogger(typeof(EstimationsEndpoints)).LogInformation(
                    "The estimation {estimationTicket} can't be read any more. {error}", estimationTicket, e.Message);

                return null;
            }
        }

        private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

        private static async Task<Results<Accepted<NewEstimationResponse>, ValidationProblem>> ProcessNewEstimation(
            NewEstimationRequest request,
            [FromHeader(Name = Idempotency.HeaderName)] string? idempotencyKey,
            ClaimsPrincipal user,
            StateHolderService.StateHolderServiceClient stateHolderService,
            IBus bus,
            ILoggerFactory loggerFactory)
        {
            var currentActivity = Activity.Current;
            var customerId = CustomerClaims.CustomerIdOf(user)!;

            using var customerScope = CustomerTelemetry.BeginScope(
                loggerFactory.CreateLogger(typeof(EstimationsEndpoints)), customerId);

            if (idempotencyKey is { Length: > Idempotency.MaxKeyLength })
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [Idempotency.HeaderName] = [$"The header can't be longer than {Idempotency.MaxKeyLength} characters."]
                });
            }

            // With an idempotency key, the same customer and key always name the same case, so a request sent again
            // (a retry after a timeout) returns the first ticket instead of starting a second case.
            // The body of the repeated request is not compared with the first one.
            var caseNumber = string.IsNullOrEmpty(idempotencyKey)
                ? Guid.NewGuid()
                : Idempotency.CaseNumberFor(customerId, idempotencyKey);

            var token = caseNumber.ToString("D");

            if (string.IsNullOrEmpty(idempotencyKey) || !await IsKnownCase(stateHolderService, token, customerId))
            {
                // Two copies racing past the check above are still one case: the saga ignores
                // a request for a case that is already running.
                await bus.Send(new EstimationRequested
                {
                    CaseNumber = caseNumber,
                    CustomerId = customerId,
                    CallbackUrl = request.CallBackUri,
                    ImageUrls = request.ImageUrls
                });
            }

            return TypedResults.Accepted(
                $"/estimations/{token}",
                new NewEstimationResponse
                {
                    Token = token,
                    OperationId = currentActivity?.RootId ?? "N/A"
                });
        }

        // A case that already finished has no saga left to ignore the repeated request, so ask the state holder.
        private static async Task<bool> IsKnownCase(
            StateHolderService.StateHolderServiceClient stateHolderService, string ticket, string customerId)
        {
            var stateResponse = await stateHolderService.StateQueryAsync(
                new StateRequest { Ticket = ticket, CustomerId = customerId });

            return stateResponse.Found;
        }
    }
}
