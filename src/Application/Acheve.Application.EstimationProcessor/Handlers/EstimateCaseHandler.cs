using System.Net.Mime;
using System.Text;
using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Bus;
using Rebus.Handlers;

namespace Acheve.Application.EstimationProcessor.Handlers
{
    public class EstimateCaseHandler(
        IBus bus,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        CallbackSigner callbackSigner,
        ILogger<EstimateCaseHandler> logger) : IHandleMessages<EstimateCase>
    {
        private readonly string _apiUrl = configuration.GetServiceUrl(Constants.Resources.Api);

        public async Task Handle(EstimateCase message)
        {
            logger.LogInformation(
                "New request for external estimation. Case number: {caseNumber}.",
                message.CaseNumber);

            var client = httpClientFactory.CreateClient("estimations");

            try
            {
                // Inside the try: a missing attachment is reported as an estimation error
                // instead of failing the message and leaving the case waiting.
                var contentString = System.Text.Json.JsonSerializer.Serialize(new
                {
                    message.CaseNumber,
                    CallbackUrl = callbackSigner.EstimationUrl(_apiUrl, message.CaseNumber, CallbackTracing.CurrentTraceParent),
                    Metadata = await ReadMetadata(message.Metadata)
                });

                using var request = new HttpRequestMessage(HttpMethod.Post, "Estimation")
                {
                    Content = new StringContent(contentString, Encoding.UTF8, MediaTypeNames.Application.Json)
                };

                // One estimation per case, however many times the request is sent
                // (HTTP retries, a redelivered message).
                request.Headers.Add(Idempotency.HeaderName, message.CaseNumber.ToString("D"));

                using var response = await client.SendAsync(request);

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

        // The messages carry data bus tickets; the external service needs the analyses themselves.
        private async Task<List<object>> ReadMetadata(IEnumerable<ImageMetadata> images)
        {
            var metadata = new List<object>();

            foreach (var image in images)
            {
                using var reader = new StreamReader(await bus.Advanced.DataBus.OpenRead(image.MetadataTicket));

                metadata.Add(new
                {
                    image.ImageId,
                    Metadata = await reader.ReadToEndAsync()
                });
            }

            return metadata;
        }

        private async Task ProcessResponse(EstimateCase message, HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Request for external estimation for case number {caseNumber} sent successfully.",
                    message.CaseNumber);

                await bus.Send(new EstimationRequestAccepted
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
