using System.Text;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Rebus.Bus;

namespace Acheve.Application.Api.Features.ImageAnalysisCallback
{
    public static class ImageAnalysisCallbackEndpoints
    {
        public static IEndpointRouteBuilder MapImageAnalysisCallback(this IEndpointRouteBuilder app)
        {
            app.MapPost("/callbacks/image-analysis/{caseNumber:guid}/images/{imageId:int}", ReceiveImageAnalysis);

            return app;
        }

        private static async Task<Results<Accepted, UnauthorizedHttpResult>> ReceiveImageAnalysis(
            Guid caseNumber,
            int imageId,
            [FromQuery(Name = CallbackSigner.TraceParentParameter)] string? traceParent,
            [FromQuery(Name = CallbackSigner.ExpiresParameter)] long? expires,
            [FromQuery(Name = CallbackSigner.SignatureParameter)] string? signature,
            ImageAnalysisResult request,
            IBus bus,
            CallbackSigner callbackSigner,
            CallbackTracing callbackTracing,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger(typeof(ImageAnalysisCallbackEndpoints));

            // Only the callback URL we sent to the external system carries a valid signature, and only until it expires.
            if (!callbackSigner.IsValidImageAnalysis(caseNumber, imageId, traceParent, expires, signature))
            {
                logger.LogWarning(
                    "Rejected an image analysis callback for case number {caseNumber} and image {imageId}: invalid or expired signature.",
                    caseNumber,
                    imageId);

                return TypedResults.Unauthorized();
            }

            // From here on, logs and the message we send belong to the case's trace.
            using var activity = callbackTracing.StartCallbackActivity(
                "image analysis callback", traceParent, caseNumber, imageId);

            logger.LogInformation(
                "Response received from the external image processor service for case number {caseNumber} and image {imageId}",
                caseNumber,
                imageId);

            using var source = new MemoryStream(Encoding.UTF8.GetBytes(request.Metadata));
            var attachment = await bus.Advanced.DataBus.CreateAttachment(source);

            var message = new ImageAnalysed
            {
                CaseNumber = caseNumber,
                ImageId = imageId,
                MetadataTicket = attachment.Id
            };

            await bus.Send(message);

            return TypedResults.Accepted((string?)null);
        }
    }
}
