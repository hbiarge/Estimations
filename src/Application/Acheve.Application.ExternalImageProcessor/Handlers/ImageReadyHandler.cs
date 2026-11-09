using Acheve.Common.Messages;
using Acheve.Common.Shared;
using Rebus.Bus;
using Rebus.DataBus;
using Rebus.Handlers;

namespace Acheve.Application.ExternalImageProcessor.Handlers
{
    public class ImageReadyHandler : IHandleMessages<ImageReady>
    {
        private readonly IBus _bus;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiUrl;
        private readonly CallbackSigner _callbackSigner;
        private readonly ILogger<ImageReadyHandler> _logger;

        public ImageReadyHandler(
            IBus bus,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            CallbackSigner callbackSigner,
            ILogger<ImageReadyHandler> logger)
        {
            _bus = bus;
            _httpClientFactory = httpClientFactory;
            _apiUrl = configuration.GetServiceUrl(Constants.Resources.Api);
            _callbackSigner = callbackSigner;
            _logger = logger;
        }

        public async Task Handle(ImageReady message)
        {
            _logger.LogInformation(
                "New request to analyze image for case number {caseNumber}. ImageId: {imageId}, ImageTicket {ticket}",
                message.CaseNumber,
                message.ImageId,
                message.ImageTicket);

            var client = _httpClientFactory.CreateClient("process");

            var callbackUrl = _callbackSigner.ImageAnalysisUrl(_apiUrl, message.CaseNumber, message.ImageId);

            using var content = new MultipartFormDataContent
            {
                {new StringContent(message.CaseNumber.ToString("D")), "CaseNumber"},
                {new StringContent(message.ImageId.ToString("G")), "ImageId"},
                {new StringContent(callbackUrl), "CallbackUrl"},
            };

            try
            {
                // Inside the try: a missing attachment is reported as an analysis error
                // instead of failing the message and leaving the case waiting.
                var imageStream = await _bus.Advanced.DataBus.OpenRead(message.ImageTicket);
                content.Add(new StreamContent(imageStream), "Image", message.ImageName);

                using var response = await client.PostAsync("ImageProcess", content);

                await ProcessResponse(message, response);
            }
            catch (Exception e)
            {
                _logger.LogWarning(
                    "Error sending the request to analize image {imageId} for case number {caseNumber}. {imageProcessError}",
                    message.ImageId,
                    message.CaseNumber,
                    e.Message);

                await _bus.Send(new UnableToAnalizeImage
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId,
                    Error = e.Message
                });
            }
        }

        private async Task ProcessResponse(ImageReady message, HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Request to analize image {imageId} for case number {caseNumber} sent successfully",
                    message.ImageId,
                    message.CaseNumber);

                await _bus.Send(new AwaitImageToBeProcessed
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId
                });
            }
            else
            {
                _logger.LogWarning(
                    "Error sending the request to analize image {imageId} for case number {caseNumber}: StatusCode: {StatusCode}",
                    message.ImageId,
                    message.CaseNumber,
                    response.StatusCode);

                await _bus.Send(new UnableToAnalizeImage
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = message.ImageId,
                    Error = $"Response error. Status code: {response.StatusCode}"
                });
            }
        }
    }
}
