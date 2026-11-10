using System.Text;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.Http.Extensions;
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
            [FromQuery(Name = CallbackSigner.SignatureParameter)] string? signature,
            ImageAnalysisResult request,
            HttpRequest httpRequest,
            IBus bus,
            CallbackSigner callbackSigner,
            CallbackTracing callbackTracing,
            ILoggerFactory loggerFactory)
        {
            var logger = loggerFactory.CreateLogger(typeof(ImageAnalysisCallbackEndpoints));

            // Only the callback URL we sent to the external system carries a valid signature.
            if (!callbackSigner.IsValidImageAnalysis(caseNumber, imageId, traceParent, signature))
            {
                logger.LogWarning(
                    "Rejected an image analysis callback for case number {caseNumber} and image {imageId}: invalid signature.",
                    caseNumber,
                    imageId);

                return TypedResults.Unauthorized();
            }

            // From here on, logs and the message we send belong to the case's trace.
            using var activity = callbackTracing.StartCallbackActivity(
                "image analysis callback", traceParent, caseNumber, imageId);

            RandomFailureGenerator.RandomFail(
                failThreshold: Constants.FailureThresholds.ApplicationReceivingExternalImages, // Exception is thrown if a random value between 0 and 1 is greater or equal this value
                url: httpRequest.GetDisplayUrl(),
                logger: logger);

            logger.LogInformation(
                "Response received from the external image processor service for case number {caseNumber} and image {imageId}",
                caseNumber,
                imageId);

            using var source = new MemoryStream(Encoding.UTF8.GetBytes(request.Metadata));
            var attachment = await bus.Advanced.DataBus.CreateAttachment(source);

            var message = new ImageAnalized
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
