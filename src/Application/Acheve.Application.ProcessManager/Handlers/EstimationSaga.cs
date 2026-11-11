using Acheve.Common.Messages;
using Rebus.Bus;
using Rebus.Handlers;
using Rebus.Pipeline;
using Rebus.Sagas;
using Rebus.Sagas.Idempotent;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga(
        IBus bus,
        ICaseAttachments attachments,
        EstimationMetrics metrics,
        TimeProvider timeProvider,
        ILogger<EstimationSaga> logger) :
        IdempotentSaga<EstimationState>,
        IAmInitiatedBy<EstimationRequested>,
        // Stage 1: Download images
        IHandleMessages<ImageDownloaded>,
        IHandleMessages<UnableToDownloadImage>,
        IHandleMessages<ImageDownloadTimeout>,
        // Stage 2: Analyse images
        IHandleMessages<ImageAnalysisAccepted>,
        IHandleMessages<ImageAnalysisTimeout>,
        IHandleMessages<ImageAnalysed>,
        IHandleMessages<UnableToAnalyseImage>,
        // Stage 3: Estimate
        IHandleMessages<EstimationRequestAccepted>,
        IHandleMessages<EstimationTimeout>,
        IHandleMessages<EstimationCompleted>,
        IHandleMessages<UnableToEstimate>,
        // Stage 4: Notify client
        IHandleMessages<NotificationCompleted>,
        IHandleMessages<UnableToNotify>,
        IHandleMessages<NotificationTimeout>
    {
        protected override void CorrelateMessages(
            ICorrelationConfig<EstimationState> config)
        {
            // events of interest
            config.Correlate<EstimationRequested>(m => m.CaseNumber, d => d.CaseNumber);

            // Stage 1: Download images
            config.Correlate<ImageDownloaded>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<UnableToDownloadImage>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<ImageDownloadTimeout>(m => m.CaseNumber, d => d.CaseNumber);

            // Stage 2: Analyse images
            config.Correlate<ImageAnalysisAccepted>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<ImageAnalysisTimeout>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<ImageAnalysed>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<UnableToAnalyseImage>(m => m.CaseNumber, d => d.CaseNumber);

            // Stage 3: Estimate
            config.Correlate<EstimationRequestAccepted>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<EstimationTimeout>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<EstimationCompleted>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<UnableToEstimate>(m => m.CaseNumber, d => d.CaseNumber);

            // Stage 4: Notify client
            config.Correlate<NotificationCompleted>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<UnableToNotify>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<NotificationTimeout>(m => m.CaseNumber, d => d.CaseNumber);
        }

        public async Task Handle(EstimationRequested message)
        {
            // A redelivered request must not restart a case that is already running.
            if (!IsNew)
            {
                logger.LogWarning(
                    "Case number {caseNumber}. Estimation already requested. Ignoring duplicate request.",
                    message.CaseNumber);

                return;
            }

            var imageUrls = message.ImageUrls ?? [];

            logger.LogInformation(
                "New estimation requested {caseNumber} from client {clientId}. Should be notified in {callbackUrl}. Trying to download {imageNumber} image(s).",
                message.CaseNumber,
                message.ClientId,
                message.CallbackUrl,
                imageUrls.Count);

            Data.CaseNumber = message.CaseNumber;
            Data.ClientId = message.ClientId;
            Data.CallbackUrl = message.CallbackUrl;
            Data.StartedAt = timeProvider.GetUtcNow();
            Data.Images = imageUrls.Select((url, index) => new CaseImage
            {
                Id = index,
                Url = url ?? string.Empty,
                Extension = GetImageExtension(url)
            }).ToArray();

            RecordWhenHandled(metrics.CaseStarted);

            await ChangeState(EstimationStates.WaitingForImagesToBeDownloaded);

            // The Api rejects a request without images, but a case that got here without any would wait forever.
            if (Data.Images.Count == 0)
            {
                await FailEstimation("The request has no images", FailureReasons.NoValidImages);
                return;
            }

            foreach (var image in Data.Images)
            {
                await bus.Send(new DownloadImage
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = image.Id,
                    ImageUrl = image.Url
                });

                // The wait starts with the command, so a download that is never answered still ends.
                await bus.DeferLocal(
                    CaseImage.DownloadWaitTime,
                    new ImageDownloadTimeout
                    {
                        CaseNumber = message.CaseNumber,
                        ImageId = image.Id
                    });
            }
        }

        // Moves the case to a new state, records how long it stayed in the previous one and tells the StateHolder.
        private async Task ChangeState(EstimationStates newState)
        {
            var now = timeProvider.GetUtcNow();
            var previousState = Data.State;
            var previousStateChangedAt = Data.StateChangedAt;

            Data.State = newState;
            Data.StateChangedAt = now;

            if (previousStateChangedAt != default)
            {
                RecordWhenHandled(() => metrics.StateLeft(previousState, now - previousStateChangedAt));
            }

            await bus.Send(new EstimationStateChanged
            {
                CaseNumber = Data.CaseNumber,
                ClientId = Data.ClientId,
                State = Data.State,
                Result = Data.EstimationFinished
                    ? Data.FailureReason is null ? CaseResults.Success : CaseResults.Failure
                    : null,
                FailureReason = Data.ExternalEstimationError,
                EstimationTicket = Data.ExternalEstimationTicket,
                SkippedImages = SkippedImages()
            });
        }

        // The images left out of the estimation. Empty while any image is still pending.
        private SkippedImage[] SkippedImages()
        {
            if (Data.Images.Any(x => !x.Analyzed))
            {
                return [];
            }

            return Data.Images
                .Where(x => !x.AvailableToEstimate)
                .Select(x => new SkippedImage
                {
                    ImageId = x.Id,
                    Url = x.Url,
                    Reason = x.SkipReason ?? string.Empty
                })
                .ToArray();
        }

        // Records a metric once Rebus has finished the message: the saga data is saved and the messages are sent.
        // A handler that fails and is retried records nothing for the failed attempt, so nothing counts twice.
        private static void RecordWhenHandled(Action record)
        {
            var transactionContext = MessageContext.Current?.TransactionContext;

            if (transactionContext is null)
            {
                record();
                return;
            }

            transactionContext.OnAck(_ =>
            {
                record();
                return Task.CompletedTask;
            });
        }

        // Returns null (and logs) when a message names an image this case doesn't have,
        // so a bad message is dropped instead of failing on every retry.
        private CaseImage? FindImage(Guid caseNumber, int imageId)
        {
            var image = Data.Images.SingleOrDefault(x => x.Id == imageId);

            if (image is null)
            {
                logger.LogWarning(
                    "Case number {caseNumber}. The case has no image {imageId}. Ignoring the message.",
                    caseNumber,
                    imageId);
            }

            return image;
        }

        // The extension without the dot ("png"), ignoring any query string in the URL. Empty when there is none.
        public static string GetImageExtension(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return string.Empty;
            }

            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;

            return Path.GetExtension(path).TrimStart('.');
        }
    }
}
