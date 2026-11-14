using Acheve.Common.Messages;

namespace Acheve.Application.ProcessManager.Handlers
{
    public partial class EstimationSaga
    {
        // True while the case has a result and the client's answer is pending.
        private bool WaitingForNotification =>
            Data.State is EstimationStates.EstimationReady or EstimationStates.EstimationError;

        public async Task Handle(NotificationCompleted message)
        {
            // Any attempt that reached the client ends the case, even one the saga had stopped waiting for.
            if (!WaitingForNotification)
            {
                LogIgnoredNotificationMessage(message.CaseNumber, nameof(NotificationCompleted));
                return;
            }

            logger.LogInformation(
                "Case number {caseNumber}. Estimation successfully notified to client {clientId} in {callbackUrl} (attempt {attempt}).",
                message.CaseNumber,
                Data.ClientId,
                Data.CallbackUrl,
                message.Attempt);

            await FinishCase(EstimationStates.ClientNotified);
        }

        public async Task Handle(UnableToNotify message)
        {
            // An answer for an earlier attempt: the saga already moved on to the next one.
            if (!WaitingForNotification || message.Attempt != Data.NotificationAttempt)
            {
                LogIgnoredNotificationMessage(message.CaseNumber, nameof(UnableToNotify));
                return;
            }

            logger.LogWarning(
                "Case number {caseNumber}. Unable to notify to {clientId} in url {callbackUrl} (attempt {attempt}). {notificationError}",
                message.CaseNumber,
                Data.ClientId,
                Data.CallbackUrl,
                message.Attempt,
                message.Error);

            await RetryNotificationOrGiveUp();
        }

        public async Task Handle(NotificationTimeout message)
        {
            // The notifier answered meanwhile, or this is the timeout of an earlier attempt.
            if (!WaitingForNotification || message.Attempt != Data.NotificationAttempt)
            {
                return;
            }

            logger.LogWarning(
                "Case number {caseNumber}. No answer from the notifier for attempt {attempt}. [{notificationWaitTime}]",
                message.CaseNumber,
                message.Attempt,
                EstimationState.NotificationWaitTime);

            await RetryNotificationOrGiveUp();
        }

        // First notification, sent as soon as the case has a result.
        private async Task StartNotification()
        {
            Data.NotificationAttempt = 1;

            await bus.Send(NotificationCommand());

            await bus.DeferLocal(
                EstimationState.NotificationWaitTime,
                new NotificationTimeout
                {
                    CaseNumber = Data.CaseNumber,
                    Attempt = Data.NotificationAttempt
                });
        }

        private async Task RetryNotificationOrGiveUp()
        {
            if (Data.NotificationAttempt >= EstimationState.MaxNotificationAttempts)
            {
                logger.LogWarning(
                    "Case number {caseNumber}. Giving up notifying {clientId} after {attempts} attempts.",
                    Data.CaseNumber,
                    Data.ClientId,
                    Data.NotificationAttempt);

                await FinishCase(EstimationStates.ClientNotificationError);
                return;
            }

            // 15 s, 30 s, 60 s: the delay doubles with every failure.
            var delay = EstimationState.NotificationRetryDelay * Math.Pow(2, Data.NotificationAttempt - 1);

            Data.NotificationAttempt += 1;

            await bus.Defer(delay, NotificationCommand());

            await bus.DeferLocal(
                delay + EstimationState.NotificationWaitTime,
                new NotificationTimeout
                {
                    CaseNumber = Data.CaseNumber,
                    Attempt = Data.NotificationAttempt
                });
        }

        // NotifyEstimation when the case has an estimation, NotifyEstimationFailure when it failed.
        private object NotificationCommand() =>
            Data.FailureReason is null
                ? new NotifyEstimation
                {
                    CaseNumber = Data.CaseNumber,
                    CallbackUrl = Data.CallbackUrl,
                    EstimationTicket = Data.ExternalEstimationTicket!,
                    Attempt = Data.NotificationAttempt,
                    SkippedImages = SkippedImages()
                }
                : new NotifyEstimationFailure
                {
                    CaseNumber = Data.CaseNumber,
                    CallbackUrl = Data.CallbackUrl,
                    Reason = Data.ExternalEstimationError ?? string.Empty,
                    Attempt = Data.NotificationAttempt,
                    SkippedImages = SkippedImages()
                };

        private async Task FinishCase(EstimationStates finalState)
        {
            await ChangeState(finalState);
            RecordCaseFinished();
            await PublishCaseFinished();

            MarkAsComplete();
            await CleanupDataBus();

            logger.LogInformation(
                "Case number {caseNumber} finished.",
                Data.CaseNumber);
        }

        // CaseCompleted when the case has an estimation, CaseFailed when it doesn't. Published once, as the saga ends.
        private Task PublishCaseFinished()
        {
            var clientNotified = Data.State == EstimationStates.ClientNotified;

            return Data.FailureReason is null
                ? bus.Publish(new CaseCompleted
                {
                    CaseNumber = Data.CaseNumber,
                    ClientId = Data.ClientId,
                    EstimationTicket = Data.ExternalEstimationTicket!,
                    SkippedImages = SkippedImages(),
                    ClientNotified = clientNotified,
                    StartedAt = Data.StartedAt,
                    FinishedAt = Data.StateChangedAt
                })
                : bus.Publish(new CaseFailed
                {
                    CaseNumber = Data.CaseNumber,
                    ClientId = Data.ClientId,
                    FailureReason = Data.FailureReason,
                    Error = Data.ExternalEstimationError ?? string.Empty,
                    SkippedImages = SkippedImages(),
                    ClientNotified = clientNotified,
                    StartedAt = Data.StartedAt,
                    FinishedAt = Data.StateChangedAt
                });
        }

        private void LogIgnoredNotificationMessage(Guid caseNumber, string messageType)
        {
            logger.LogWarning(
                "Case number {caseNumber}. The case is not waiting for this notification answer. Ignoring {messageType}.",
                caseNumber,
                messageType);
        }

        private void RecordCaseFinished()
        {
            // The estimation result is known before the client is notified: the state was EstimationReady or EstimationError.
            var result = Data.FailureReason is null ? CaseResult.Success : CaseResult.Failure;
            var failureReason = Data.FailureReason ?? FailureReasons.None;
            var finalState = Data.State;
            TimeSpan? duration = Data.StartedAt == default ? null : Data.StateChangedAt - Data.StartedAt;

            RecordWhenHandled(() => metrics.CaseFinished(result, finalState, failureReason, duration));
        }

        // Deletes the images and their analyses. The estimation stays, so the client can still read it from
        // GET /estimations/{ticket}; the data bus sweeper deletes it when its retention time is over.
        private async Task CleanupDataBus()
        {
            foreach (var image in Data.Images)
            {
                if (string.IsNullOrEmpty(image.ImageTicket) == false)
                {
                    await attachments.Delete(image.ImageTicket);
                }

                if (string.IsNullOrEmpty(image.AnalysisTicket) == false)
                {
                    await attachments.Delete(image.AnalysisTicket);
                }
            }
        }
    }
}
