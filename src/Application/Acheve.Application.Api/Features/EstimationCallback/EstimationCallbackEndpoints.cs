using System.Text;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Rebus.Bus;

namespace Acheve.Application.Api.Features.EstimationCallback
{
    public static class EstimationCallbackEndpoints
    {
        public static IEndpointRouteBuilder MapEstimationCallback(this IEndpointRouteBuilder app)
        {
            app.MapPost("/callbacks/estimation/{caseNumber:guid}", ReceiveEstimation);

            return app;
        }

        private static async Task<Results<Accepted, UnauthorizedHttpResult>> ReceiveEstimation(
            Guid caseNumber,
            [FromQuery(Name = CallbackSigner.TraceParentParameter)] string? traceParent,
            [FromQuery(Name = CallbackSigner.ExpiresParameter)] long? expires,
            [FromQuery(Name = CallbackSigner.SignatureParameter)] string? signature,
            EstimationResult request,
            IBus bus,
            CallbackSigner callbackSigner,
            CallbackTracing callbackTracing,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger(typeof(EstimationCallbackEndpoints));

            // Only the callback URL we sent to the external system carries a valid signature, and only until it expires.
            if (!callbackSigner.IsValidEstimation(caseNumber, traceParent, expires, signature))
            {
                logger.LogWarning("Rejected an estimation callback for case number {caseNumber}: invalid or expired signature.", caseNumber);

                return TypedResults.Unauthorized();
            }

            // From here on, logs and the message we send belong to the case's trace.
            using var activity = callbackTracing.StartCallbackActivity(
                "estimation callback", traceParent, caseNumber);

            logger.LogInformation("Response received from the external estimations service for case number {caseNumber}", caseNumber);

            using var source = new MemoryStream(Encoding.UTF8.GetBytes(request.Estimation));
            var attachment = await bus.Advanced.DataBus.CreateAttachment(source);

            var message = new EstimationCompleted
            {
                CaseNumber = caseNumber,
                EstimationTicket = attachment.Id
            };

            await bus.Send(message);

            return TypedResults.Accepted((string?)null);
        }
    }
}
