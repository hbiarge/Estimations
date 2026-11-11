using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Bus;
using Rebus.Handlers;

namespace Acheve.Application.ExternalImageProcessor.Handlers
{
    public class AnalyseImageHandler(
        IBus bus,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        CallbackSigner callbackSigner,
        ILogger<AnalyseImageHandler> logger) : IHandleMessages<AnalyseImage>
    {
        private readonly string _apiUrl = configuration.GetServiceUrl(Constants.Resources.Api);

        public async Task Handle(AnalyseImage message)
        {
            logger.LogInformation(
                "New request to analyze image for case number {caseNumber}. ImageId: {imageId}, ImageTicket {ticket}",
                message.CaseNumber,
                message.ImageId,
                message.ImageTicket);

            var client = httpClientFactory.CreateClient("process");

            var callbackUrl = callbackSigner.ImageAnalysisUrl(
                _apiUrl, message.CaseNumber, message.ImageId, CallbackTracing.CurrentTraceParent);

            using var content = new MultipartFormDataContent
            {
                {new StringContent(message.CaseNumber.ToString("D")), "CaseNumber"},
                {new StringContent(message.ImageId.ToString("G")), "ImageId"},
                {new StringContent(callbackUrl), "CallbackUrl"},
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "ImageProcess") { Content = content };

            // The same image of the same case is the same request, however many times it is sent
            // (HTTP retries, a redelivered message), so the external service can analyse it only once.
            request.Headers.Add(Idempotency.HeaderName, $"{message.CaseNumber:D}:{message.ImageId:G}");

            try
            {
                // Inside the try: a missing attachment is reported as an analysis error
                // instead of failing the message and leaving the case waiting.
                var imageStream = await bus.Advanced.DataBus.OpenRead(message.ImageTicket);
                content.Add(new StreamContent(imageStream), "Image", message.ImageName);

                using var response = await client.SendAsync(request);

                await ProcessResponse(message, response);
            }
            catch (Exception e)
            {
                logger.LogWarning(
                    "Error sending the request to analyse image {imageId} for case number {caseNumber}. {imageProcessError}",
                    message.ImageId,
                    message.CaseNumber,
                    e.Message);

                await bus.Send(new UnableToAnalyseImage
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId,
                    Error = e.Message
                });
            }
        }

        private async Task ProcessResponse(AnalyseImage message, HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Request to analyse image {imageId} for case number {caseNumber} sent successfully",
                    message.ImageId,
                    message.CaseNumber);

                await bus.Send(new ImageAnalysisAccepted
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId
                });
            }
            else
            {
                logger.LogWarning(
                    "Error sending the request to analyse image {imageId} for case number {caseNumber}: StatusCode: {StatusCode}",
                    message.ImageId,
                    message.CaseNumber,
                    response.StatusCode);

                await bus.Send(new UnableToAnalyseImage
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId,
                    Error = $"Response error. Status code: {response.StatusCode}"
                });
            }
        }
    }
}
