using System.Text;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Rebus.Bus;

namespace Acheve.Application.Api.Features.ExternalEstimation
{
    [ApiController]
    [Route("[controller]")]
    public class ExternalEstimationController : ControllerBase
    {
        private readonly IBus _bus;
        private readonly CallbackSigner _callbackSigner;
        private readonly ILogger<ExternalEstimationController> _logger;

        public ExternalEstimationController(IBus bus, CallbackSigner callbackSigner, ILogger<ExternalEstimationController> logger)
        {
            _bus = bus;
            _callbackSigner = callbackSigner;
            _logger = logger;
        }

        [HttpPost("{caseNumber:guid}")]
        public async Task<IActionResult> ExternalEstimationResponse(
            Guid caseNumber,
            [FromQuery(Name = CallbackSigner.SignatureParameter)] string? signature,
            [FromBody]ExternalEstimationProcessed request)
        {
            // Only the callback URL we sent to the external system carries a valid signature.
            if (!_callbackSigner.IsValidEstimation(caseNumber, signature))
            {
                _logger.LogWarning("Rejected an estimation callback for case number {caseNumber}: invalid signature.", caseNumber);

                return Unauthorized();
            }

            RandomFailureGenerator.RandomFail(
                failThreshold: Constants.FailureThresholds.ApplicationReceivingExternalEstimations, // Exception is thrown if a random value between 0 and 1 is greater or equal this value
                url: Request.GetDisplayUrl(),
                _logger);

            _logger.LogInformation("Response received from the external estimations service for case number {caseNumber}", caseNumber);

            using var source = new MemoryStream(Encoding.UTF8.GetBytes(request.Estimation));
            var attachment = await _bus.Advanced.DataBus.CreateAttachment(source);

            var message = new EstimationCompleted
            {
                CaseNumber = caseNumber,
                EstimationTicket = attachment.Id
            };

            await _bus.Send(message);

            return Accepted();
        }
    }
}
