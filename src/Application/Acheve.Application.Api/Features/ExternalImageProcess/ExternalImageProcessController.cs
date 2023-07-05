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
        private readonly ILogger<ExternalImageProcessController> _logger;

        public ExternalImageProcessController(IBus bus, ILogger<ExternalImageProcessController> logger)
        {
            _bus = bus;
            _logger = logger;
        }

        //[Authorize(AuthenticationSchemes = "Ticket")]
        [HttpPost("{caseNumber}/images/{imageId}")]
        public async Task<IActionResult> ProcessNewEstimation(Guid caseNumber, int imageId, [FromBody]ExternalImageProcessed request)
        {
            RandomFailureGenerator.RandomFail(
                failThreshold: Constants.FailureThresholds.ApplicationReceivingExternalImages, // Exception is thrown if a random value between 0 and 1 is greater or equal this value
                url: Request.GetDisplayUrl(), 
                logger: _logger);

            _logger.LogInformation(
                "Response received from the external image processor service for case number {caseNumber} and image {imageId}", 
                caseNumber, 
                imageId);

            var source = new MemoryStream(new byte[1024]);
            source.Write(Encoding.UTF8.GetBytes(request.Metadata));
            source.Position = 0;
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
