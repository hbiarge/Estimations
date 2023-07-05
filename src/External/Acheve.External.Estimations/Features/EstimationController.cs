using System.Net.Mime;
using System.Text;
using Acheve.Common.Shared;
using Acheve.External.Shared;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace Acheve.External.Estimations.Features
{
    [ApiController]
    [Route("[controller]")]
    public class EstimationController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<EstimationController> _logger;
        private readonly IBackgroundTaskQueue _queue;

        public EstimationController(
            IHttpClientFactory httpClientFactory,
            ILogger<EstimationController> logger,
            IBackgroundTaskQueue queue)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _queue = queue;
        }

        [HttpPost]
        public async Task<IActionResult> ProcessEstimation([FromBody] EstimationModel model)
        {
            if (model.CaseNumber is null || model.CallbackUrl is null)
            {
                return BadRequest();
            }

            RandomFailureGenerator.RandomFail(
                failThreshold: Constants.FailureThresholds.ExternalEstimationsProcessing, // Exception is thrown if a random value between 0 and 1 is greater or equal this value
                url: Request.GetDisplayUrl(),
                logger: _logger);

            _logger.LogInformation(
                "Received new estimation request. Case number: {caseNumber}; Callback: {callback}",
                model.CaseNumber,
                model.CallbackUrl);

            await _queue.QueueBackgroundWorkItemAsync(
                _ => NotifyBack(model.CaseNumber, model.CallbackUrl, CancellationToken.None));

            _logger.LogInformation(
                "Estimation request queued. Case number: {caseNumber}", 
                model.CaseNumber);

            return NoContent();
        }

        private async ValueTask NotifyBack(
            string caseNumber,
            string callbackUrl,
            CancellationToken cancellationToken)
        {
            // Simulate some time to process
            await Task.Delay(10_000, cancellationToken);

            var client = _httpClientFactory.CreateClient("EstimationConfirmation");

            var contentString = System.Text.Json.JsonSerializer.Serialize(new
            {
                Estimation = $"This is the estimation for the case {caseNumber}"
            });

            await client.PostAsync(
                new Uri(callbackUrl),
                new StringContent(contentString, Encoding.UTF8, MediaTypeNames.Application.Json),
                cancellationToken);

            _logger.LogInformation(
                "Estimation notified back. Case number: {caseNumber}",
                caseNumber);
        }
    }
}
