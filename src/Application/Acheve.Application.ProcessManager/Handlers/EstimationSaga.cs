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
        EstimationMetrics metrics,
        TimeProvider timeProvider,
        ILogger<EstimationSaga> logger) :
        IdempotentSaga<EstimationState>,
        IAmInitiatedBy<EstimationRequested>,
        // Stage 1: Download images
        IHandleMessages<ImageDownloaded>,
        IHandleMessages<UnableToDownloadImage>,
        // Stage 2: Analyse images
        IHandleMessages<AwaitImageToBeProcessed>,
        IHandleMessages<StillAwaitingImageToBeProcessed>,
        IHandleMessages<ImageAnalized>,
        IHandleMessages<UnableToAnalizeImage>,
        // Stage 3: Estimate
        IHandleMessages<AwaitEstimationToBeProcessed>,
        IHandleMessages<StillAwaitingEstimationToBeProcessed>,
        IHandleMessages<EstimationCompleted>,
        IHandleMessages<UnableToEstimate>,
        // Stage 4: Notify client
        IHandleMessages<NotificationCompleted>,
        IHandleMessages<UnableToNotify>
    {
        protected override void CorrelateMessages(
            ICorrelationConfig<EstimationState> config)
        {
            // events of interest
            config.Correlate<EstimationRequested>(m => m.CaseNumber, d => d.CaseNumber);

            // Stage 1: Download images
            config.Correlate<ImageDownloaded>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<UnableToDownloadImage>(m => m.CaseNumber, d => d.CaseNumber);

            // Stage 2: Analyse images
            config.Correlate<AwaitImageToBeProcessed>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<StillAwaitingImageToBeProcessed>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<ImageAnalized>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<UnableToAnalizeImage>(m => m.CaseNumber, d => d.CaseNumber);

            // Stage 3: Estimate
            config.Correlate<AwaitEstimationToBeProcessed>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<StillAwaitingEstimationToBeProcessed>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<EstimationCompleted>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<UnableToEstimate>(m => m.CaseNumber, d => d.CaseNumber);

            // Stage 4: Notify client
            config.Correlate<NotificationCompleted>(m => m.CaseNumber, d => d.CaseNumber);
            config.Correlate<UnableToNotify>(m => m.CaseNumber, d => d.CaseNumber);
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

            logger.LogInformation(
                "New estimation requested {caseNumber} from client {clientId}. Should be notified in {callbackUrl}. Trying to download {imageNumber} image(s).",
                message.CaseNumber,
                message.ClientId,
                message.CallbackUrl,
                message.ImageUrls.Count);

            Data.CaseNumber = message.CaseNumber;
            Data.ClientId = message.ClientId;
            Data.CallbackUrl = message.CallbackUrl;
            Data.StartedAt = timeProvider.GetUtcNow();
            Data.Images = message.ImageUrls.Select((url, index) => new CaseImage
            {
                Id = index,
                Url = url,
                Extension = GetImageExtension(url)
            }).ToArray();

            RecordWhenHandled(metrics.CaseStarted);

            await ChangeState(EstimationStates.WaitingForImagesToBeDownloaded);

            foreach (var image in Data.Images)
            {
                await bus.Send(new ImageUrlReceived
                {
                    CaseNumber = message.CaseNumber,
                    ImageId = image.Id,
                    ImageUrl = image.Url
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
                State = Data.State
            });
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

        // The extension without the dot ("png"), ignoring any query string in the URL.
        public static string GetImageExtension(string url)
        {
            var path = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsolutePath : url;

            return Path.GetExtension(path).TrimStart('.');
        }
    }
}
