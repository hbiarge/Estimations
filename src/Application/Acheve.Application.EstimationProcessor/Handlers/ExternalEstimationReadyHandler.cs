using System.Net.Mime;
using System.Text;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Bus;
using Rebus.Handlers;

namespace Acheve.Application.EstimationProcessor.Handlers
{
    public class ExternalEstimationReadyHandler(
        IBus bus,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        CallbackSigner callbackSigner,
        ILogger<ExternalEstimationReadyHandler> logger) : IHandleMessages<ExternalEstimationReady>
    {
        private readonly string _apiUrl = configuration.GetServiceUrl(Constants.Resources.Api);

        public async Task Handle(ExternalEstimationReady message)
        {
            logger.LogInformation(
                "New request for external estimation. Case number: {caseNumber}.",
                message.CaseNumber);

            var client = httpClientFactory.CreateClient("estimations");

            var contentString = System.Text.Json.JsonSerializer.Serialize(new
            {
                message.CaseNumber,
                CallbackUrl = callbackSigner.EstimationUrl(_apiUrl, message.CaseNumber, CallbackTracing.CurrentTraceParent),
                message.Metadata
            });
            var content = new StringContent(contentString, Encoding.UTF8, MediaTypeNames.Application.Json);

            try
            {
                var response = await client.PostAsync("Estimation", content);

                await ProcessResponse(message, response);
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    "Error sending the request for external estimation for case number {caseNumber}. {externalEstimationError}",
                    message.CaseNumber,
                    e.Message);

                await bus.Send(new UnableToEstimate
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
                logger.LogInformation(
                    "Request for external estimation for case number {caseNumber} sent successfully.",
                    message.CaseNumber);

                await bus.Send(new AwaitEstimationToBeProcessed
                {
                    CaseNumber = message.CaseNumber
                });
            }
            else
            {
                logger.LogWarning(
                    "Error sending the request for external estimation for case number {caseNumber}: StatusCode: {StatusCode}",
                    message.CaseNumber,
                    response.StatusCode);

                await bus.Send(new UnableToEstimate
                {
                    CaseNumber = message.CaseNumber,
                    Error = $"Response error. Status code: {response.StatusCode}"
                });
            }
        }
    }
}
