using System.Text;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Rebus.Bus;

namespace Acheve.Application.Api.Features.ExternalImageProcess
{
    [ApiController]
    [Route("[controller]")]
    public class ExternalImageProcessController : ControllerBase
    {
        private readonly IBus _bus;
        private readonly CallbackSigner _callbackSigner;
        private readonly ILogger<ExternalImageProcessController> _logger;

        public ExternalImageProcessController(IBus bus, CallbackSigner callbackSigner, ILogger<ExternalImageProcessController> logger)
        {
            _bus = bus;
            _callbackSigner = callbackSigner;
            _logger = logger;
        }

        [HttpPost("{caseNumber:guid}/images/{imageId:int}")]
        public async Task<IActionResult> ProcessNewEstimation(
            Guid caseNumber,
            int imageId,
            [FromQuery(Name = CallbackSigner.SignatureParameter)] string? signature,
            [FromBody]ExternalImageProcessed request)
        {
            // Only the callback URL we sent to the external system carries a valid signature.
            if (!_callbackSigner.IsValidImageAnalysis(caseNumber, imageId, signature))
            {
                _logger.LogWarning(
                    "Rejected an image analysis callback for case number {caseNumber} and image {imageId}: invalid signature.",
                    caseNumber,
                    imageId);

                return Unauthorized();
            }

            RandomFailureGenerator.RandomFail(
                failThreshold: Constants.FailureThresholds.ApplicationReceivingExternalImages, // Exception is thrown if a random value between 0 and 1 is greater or equal this value
                url: Request.GetDisplayUrl(), 
                logger: _logger);

            _logger.LogInformation(
                "Response received from the external image processor service for case number {caseNumber} and image {imageId}", 
                caseNumber, 
                imageId);

            using var source = new MemoryStream(Encoding.UTF8.GetBytes(request.Metadata));
            var attachment = await _bus.Advanced.DataBus.CreateAttachment(source);

            var message = new ImageAnalized
            {
                CaseNumber = caseNumber,
                ImageId = imageId,
                MetadataTicket = attachment.Id
            };

            await _bus.Send(message);

            return Accepted();
        }
    }
}
