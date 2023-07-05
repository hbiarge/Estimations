using System.Net.Mime;
using System.Text;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Microsoft.Extensions.Options;
using Rebus.Bus;
using Rebus.Handlers;

namespace Acheve.Application.EstimationProcessor.Handlers
{
    public class ExternalEstimationReadyHandler : IHandleMessages<ExternalEstimationReady>
    {
        private readonly IBus _bus;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ServicesConfiguration _servicesConfiguration;
        private readonly ILogger<ExternalEstimationReadyHandler> _logger;

        public ExternalEstimationReadyHandler(
            IBus bus,
            IHttpClientFactory httpClientFactory,
            IOptions<ServicesConfiguration> servicesConfiguration,
            ILogger<ExternalEstimationReadyHandler> logger)
        {
            _bus = bus;
            _httpClientFactory = httpClientFactory;
            _servicesConfiguration = servicesConfiguration.Value;
            _logger = logger;
        }

        public async Task Handle(ExternalEstimationReady message)
        {
            _logger.LogInformation(
                "New request for external estimation. Case number: {caseNumber}.",
                message.CaseNumber);

            var client = _httpClientFactory.CreateClient("estimations");

            var contentString = System.Text.Json.JsonSerializer.Serialize(new
            {
                message.CaseNumber,
                CallbackUrl = $"{_servicesConfiguration.Api!.BaseUrl}/ExternalEstimation/{message.CaseNumber:D}",
                message.Metadata
            });
            var content = new StringContent(contentString, Encoding.UTF8, MediaTypeNames.Application.Json);

            try
            {
                var response = await client.PostAsync(
                    new Uri($"{_servicesConfiguration.Estimations!.BaseUrl}/Estimation"),
                    content);

                await ProcessResponse(message, response);
            }
            catch (Exception e)
            {
                _logger.LogWarning(
                    "Error sending the request for external estimation for case number {caseNumber}. {externalEstimationError}",
                    message.CaseNumber,
                    e.Message);

                await _bus.Send(new UnableToEstimate
                {
                    CaseNumber = message.CaseNumber,
                    Error = e.Message
                });
            }
        }

        private async Task ProcessResponse(ExternalEstimationReady message, HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Request for external estimation for case number {caseNumber} sent successfully.",
                    message.CaseNumber);

                await _bus.Send(new AwaitEstimationToBeProcessed
                {
                    CaseNumber = message.CaseNumber
                });
            }
            else
            {
                _logger.LogWarning(
                    "Error sending the request for external estimation for case number {caseNumber}: StatusCode: {StatusCode}",
                    message.CaseNumber,
                    response.StatusCode);

                await _bus.Send(new UnableToEstimate
                {
                    CaseNumber = message.CaseNumber,
                    Error = $"Response error. Status code: {response.StatusCode}"
                });
            }
        }
    }
}
