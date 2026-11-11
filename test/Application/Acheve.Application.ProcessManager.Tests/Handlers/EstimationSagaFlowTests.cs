using Acheve.Application.ProcessManager.Handlers;
using Acheve.Application.ProcessManager.Tests.Factories;
using Acheve.Common.Messages;
using Rebus.TestHelpers;
using Rebus.TestHelpers.Events;

namespace Acheve.Application.ProcessManager.Tests.Handlers
{
    // Covers the saga's own timeouts, the notification retries, and duplicate, late or out-of-order messages.
    public sealed class EstimationSagaFlowTests : IDisposable
    {
        private readonly FakeBus _bus = new();
        private readonly Sagas.FakeCaseAttachments _attachments = new();
        private readonly SagaFixture<EstimationSaga> _sut;
        private readonly Guid _caseNumber = Guid.NewGuid();

        public EstimationSagaFlowTests()
        {
            _sut = SagaFixture.For(() => Sagas.Create(_bus, attachments: _attachments));
        }

        public void Dispose() => _sut.Dispose();

        // ---- Start ----

        [Fact]
        public void Duplicate_estimation_request_does_not_restart_the_case()
        {
            StartCase(imageCount: 2);
            _bus.Clear();

            _sut.Deliver(RequestEstimation(imageCount: 2));

            Sent<DownloadImage>().ShouldBeEmpty();
            Sent<EstimationStateChanged>().ShouldBeEmpty();
        }

        [Fact]
        public void Starting_a_case_starts_the_download_wait_of_every_image()
        {
            StartCase(imageCount: 2);

            Sent<DownloadImage>().Count.ShouldBe(2);
            var timeouts = Deferred<ImageDownloadTimeout>();
            timeouts.Count.ShouldBe(2);
            timeouts.ShouldAllBe(x => x.Delay == CaseImage.DownloadWaitTime);
            timeouts.Select(x => x.CommandMessage.ImageId).ShouldBe([0, 1], ignoreOrder: true);
        }

        [Fact]
        public void Request_with_a_null_image_url_does_not_fail_the_message()
        {
            _sut.Deliver(new EstimationRequested
            {
                CaseNumber = _caseNumber,
                ClientId = "client",
                CallbackUrl = "http://notification.test",
                ImageUrls = ["http://images.test/ok.png", null!]
            });

            _sut.HandlerExceptions.ShouldBeEmpty();
            Data.Images.Count.ShouldBe(2);
        }

        [Fact]
        public void Request_without_images_fails_at_once_instead_of_waiting_forever()
        {
            _sut.Deliver(new EstimationRequested
            {
                CaseNumber = _caseNumber,
                ClientId = "client",
                CallbackUrl = "http://notification.test",
                ImageUrls = []
            });

            Sent<DownloadImage>().ShouldBeEmpty();
            Sent<NotifyEstimationFailure>().ShouldHaveSingleItem();
            Data.State.ShouldBe(EstimationStates.EstimationError);
            Data.FailureReason.ShouldBe(FailureReasons.NoValidImages);
        }

        [Fact]
        public void Messages_for_an_unknown_image_are_ignored()
        {
            StartCase(imageCount: 1);
            _bus.Clear();

            _sut.Deliver(ImageDownloaded(7));
            _sut.Deliver(ImageAnalysed(7));
            _sut.Deliver(AnalysisAccepted(7));
            _sut.Deliver(new ImageDownloadTimeout { CaseNumber = _caseNumber, ImageId = 7 });
            _sut.Deliver(new ImageAnalysisTimeout { CaseNumber = _caseNumber, ImageId = 7 });

            _bus.Events.OfType<MessageSent>().ShouldBeEmpty();
            _bus.Events.OfType<MessageDeferredToSelf>().ShouldBeEmpty();
        }

        // ---- Stage 1: download ----

        [Fact]
        public void Duplicate_image_downloaded_sends_the_image_once_and_deletes_the_extra_copy()
        {
            StartCase(imageCount: 2);

            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(new ImageDownloaded { CaseNumber = _caseNumber, ImageId = 0, ImageTicket = "second-copy" });

            Sent<AnalyseImage>().ShouldHaveSingleItem();
            _attachments.Deleted.ShouldBe(["second-copy"]);
        }

        [Fact]
        public void Redelivery_of_the_same_ticket_does_not_delete_the_kept_image()
        {
            StartCase(imageCount: 2);

            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageDownloaded(0));

            _attachments.Deleted.ShouldBeEmpty();
        }

        [Fact]
        public void Downloaded_image_starts_its_analysis_wait()
        {
            StartCase(imageCount: 1);

            _sut.Deliver(ImageDownloaded(0));

            var deferred = Deferred<ImageAnalysisTimeout>().ShouldHaveSingleItem();
            deferred.Delay.ShouldBe(CaseImage.AnalysisWaitTime);
            deferred.CommandMessage.ImageId.ShouldBe(0);
        }

        [Fact]
        public void Image_that_is_never_downloaded_times_out_and_the_case_continues()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(AnalysisAccepted(1));
            _sut.Deliver(ImageAnalysed(1));

            _sut.Deliver(new ImageDownloadTimeout { CaseNumber = _caseNumber, ImageId = 0 });

            Data.Images.Single(x => x.Id == 0).DownloadError.ShouldBe(CaseImage.WaitTimeExceeded);
            Data.State.ShouldBe(EstimationStates.WaitingForEstimation);
            Sent<EstimateCase>().ShouldHaveSingleItem().Metadata.ShouldHaveSingleItem().ImageId.ShouldBe(1);
        }

        [Fact]
        public void Download_timeout_after_the_image_arrived_does_nothing()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(ImageDownloaded(0));
            _bus.Clear();

            _sut.Deliver(new ImageDownloadTimeout { CaseNumber = _caseNumber, ImageId = 0 });

            Data.Images.Single(x => x.Id == 0).DownloadError.ShouldBeNull();
            _bus.Events.OfType<MessageSent>().ShouldBeEmpty();
        }

        [Fact]
        public void Late_download_after_its_timeout_is_ignored_and_its_copy_deleted()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(new ImageDownloadTimeout { CaseNumber = _caseNumber, ImageId = 0 });

            _sut.Deliver(ImageDownloaded(0));

            Sent<AnalyseImage>().ShouldBeEmpty();
            _attachments.Deleted.ShouldBe(["image-0"]);
        }

        [Fact]
        public void Case_without_usable_images_fails_the_estimation()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(new UnableToDownloadImage { CaseNumber = _caseNumber, ImageId = 0, Error = "404" });
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(UnableToAnalyse(1));

            Sent<EstimateCase>().ShouldBeEmpty();
            Sent<NotifyEstimationFailure>().ShouldHaveSingleItem();
            Sent<EstimationStateChanged>().Last().State.ShouldBe(EstimationStates.EstimationError);
            Data.State.ShouldBe(EstimationStates.EstimationError);
        }

        // ---- Stage 2: analysis ----

        [Fact]
        public void Accepted_analysis_gets_the_whole_wait_again_only_once()
        {
            StartCase(imageCount: 1);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(new ImageAnalysisTimeout { CaseNumber = _caseNumber, ImageId = 0 });
            _sut.Deliver(new ImageAnalysisTimeout { CaseNumber = _caseNumber, ImageId = 0 });
            Data.Images.Single().CurrentAnalysisWaits.ShouldBe(2);

            _sut.Deliver(AnalysisAccepted(0));
            Data.Images.Single().CurrentAnalysisWaits.ShouldBe(0);

            _sut.Deliver(new ImageAnalysisTimeout { CaseNumber = _caseNumber, ImageId = 0 });
            _sut.Deliver(AnalysisAccepted(0));
            Data.Images.Single().CurrentAnalysisWaits.ShouldBe(1);
        }

        [Fact]
        public void Analysis_request_that_is_never_answered_ends_the_wait()
        {
            // The worker never sent ImageAnalysisAccepted nor UnableToAnalyseImage: the timeout started with the command still ends it.
            StartCase(imageCount: 1);
            _sut.Deliver(ImageDownloaded(0));

            for (var i = 0; i <= CaseImage.MaxAnalysisWaits; i++)
            {
                _sut.Deliver(new ImageAnalysisTimeout { CaseNumber = _caseNumber, ImageId = 0 });
            }

            Data.Images.Single().AnalysisError.ShouldBe(CaseImage.WaitTimeExceeded);
            Data.State.ShouldBe(EstimationStates.EstimationError);
            Sent<NotifyEstimationFailure>().ShouldHaveSingleItem();
        }

        [Fact]
        public void Image_analysis_timeout_continues_with_the_other_images()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(ImageAnalysed(0));
            _sut.Deliver(AnalysisAccepted(1));

            for (var i = 0; i <= CaseImage.MaxAnalysisWaits; i++)
            {
                _sut.Deliver(new ImageAnalysisTimeout { CaseNumber = _caseNumber, ImageId = 1 });
            }

            // One wait per image when it was downloaded, then one more for every check that waits again.
            Deferred<ImageAnalysisTimeout>().Count.ShouldBe(2 + CaseImage.MaxAnalysisWaits);
            Data.Images.Single(x => x.Id == 1).AnalysisError.ShouldBe(CaseImage.WaitTimeExceeded);
            Data.State.ShouldBe(EstimationStates.WaitingForEstimation);

            var estimation = Sent<EstimateCase>().ShouldHaveSingleItem();
            estimation.Metadata.ShouldHaveSingleItem().ImageId.ShouldBe(0);
        }

        [Fact]
        public void Late_image_analysis_after_timeout_does_not_request_a_second_estimation_and_deletes_its_copy()
        {
            StartCase(imageCount: 1);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(AnalysisAccepted(0));

            for (var i = 0; i <= CaseImage.MaxAnalysisWaits; i++)
            {
                _sut.Deliver(new ImageAnalysisTimeout { CaseNumber = _caseNumber, ImageId = 0 });
            }

            _sut.Deliver(ImageAnalysed(0));

            Sent<EstimateCase>().ShouldBeEmpty();
            Sent<NotifyEstimationFailure>().ShouldHaveSingleItem();
            _attachments.Deleted.ShouldBe(["metadata-0"]);
        }

        [Fact]
        public void Duplicate_image_analysis_requests_the_estimation_once()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(ImageAnalysed(0));
            _sut.Deliver(ImageAnalysed(1));

            _sut.Deliver(new ImageAnalysed { CaseNumber = _caseNumber, ImageId = 1, MetadataTicket = "duplicate" });
            _sut.Deliver(UnableToAnalyse(0));

            Sent<EstimateCase>().ShouldHaveSingleItem().Metadata.Count.ShouldBe(2);
            _attachments.Deleted.ShouldBe(["duplicate"]);
        }

        [Fact]
        public void Image_analysis_before_the_acceptance_is_kept_and_the_late_acceptance_changes_nothing()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(ImageAnalysed(0));

            _sut.Deliver(AnalysisAccepted(0));

            Data.Images.Single(x => x.Id == 0).AnalysisAccepted.ShouldBeFalse();
            Data.Images.Single(x => x.Id == 0).AnalysisTicket.ShouldBe("metadata-0");
        }

        // ---- Stage 3: estimation ----

        [Fact]
        public void Asking_for_the_estimation_starts_its_wait()
        {
            StartCaseWaitingForEstimation();

            Deferred<EstimationTimeout>().ShouldHaveSingleItem().Delay.ShouldBe(EstimationState.EstimationWaitTime);
        }

        [Fact]
        public void Accepted_estimation_gets_the_whole_wait_again_only_once()
        {
            StartCaseWaitingForEstimation();
            _sut.Deliver(new EstimationTimeout { CaseNumber = _caseNumber });
            _sut.Deliver(new EstimationTimeout { CaseNumber = _caseNumber });

            _sut.Deliver(new EstimationRequestAccepted { CaseNumber = _caseNumber });
            Data.CurrentExternalEstimationWaits.ShouldBe(0);

            _sut.Deliver(new EstimationTimeout { CaseNumber = _caseNumber });
            _sut.Deliver(new EstimationRequestAccepted { CaseNumber = _caseNumber });
            Data.CurrentExternalEstimationWaits.ShouldBe(1);
        }

        [Fact]
        public void Estimation_timeout_after_the_estimation_arrived_does_nothing()
        {
            StartCaseWaitingForEstimation();
            _sut.Deliver(EstimationCompleted());
            _bus.Clear();

            _sut.Deliver(new EstimationTimeout { CaseNumber = _caseNumber });

            _bus.Events.OfType<MessageSent>().ShouldBeEmpty();
            _bus.Events.OfType<MessageDeferredToSelf>().ShouldBeEmpty();
            Data.State.ShouldBe(EstimationStates.EstimationReady);
        }

        [Fact]
        public void Estimation_timeout_notifies_an_error_to_the_client()
        {
            StartCaseWaitingForEstimation();
            _sut.Deliver(new EstimationRequestAccepted { CaseNumber = _caseNumber });

            for (var i = 0; i <= EstimationState.MaxEstimationWaits; i++)
            {
                _sut.Deliver(new EstimationTimeout { CaseNumber = _caseNumber });
            }

            var failure = Sent<NotifyEstimationFailure>().ShouldHaveSingleItem();
            failure.Reason.ShouldBe(CaseImage.WaitTimeExceeded);
            failure.Attempt.ShouldBe(1);
            Sent<UnableToEstimate>().ShouldBeEmpty();
            Data.State.ShouldBe(EstimationStates.EstimationError);
        }

        [Fact]
        public void Estimation_rejected_by_the_external_service_notifies_an_error_to_the_client()
        {
            StartCaseWaitingForEstimation();

            _sut.Deliver(new UnableToEstimate { CaseNumber = _caseNumber, Error = "503" });

            Sent<NotifyEstimationFailure>().ShouldHaveSingleItem().Reason.ShouldBe("503");
            Data.FailureReason.ShouldBe(FailureReasons.EstimationRejected);
        }

        [Fact]
        public void Estimation_before_it_was_asked_for_is_ignored_and_deleted()
        {
            StartCase(imageCount: 1);

            _sut.Deliver(EstimationCompleted());

            Sent<NotifyEstimation>().ShouldBeEmpty();
            Data.State.ShouldBe(EstimationStates.WaitingForImagesToBeDownloaded);
            _attachments.Deleted.ShouldBe(["estimation"]);
        }

        [Fact]
        public void Late_estimation_after_timeout_is_ignored_and_deleted()
        {
            StartCaseWaitingForEstimation();
            _sut.Deliver(new EstimationRequestAccepted { CaseNumber = _caseNumber });

            for (var i = 0; i <= EstimationState.MaxEstimationWaits; i++)
            {
                _sut.Deliver(new EstimationTimeout { CaseNumber = _caseNumber });
            }

            _sut.Deliver(EstimationCompleted());

            Sent<NotifyEstimation>().ShouldBeEmpty();
            Data.State.ShouldBe(EstimationStates.EstimationError);
            _attachments.Deleted.ShouldBe(["estimation"]);
        }

        [Fact]
        public void Duplicate_estimation_notifies_the_client_once_and_keeps_the_first_estimation()
        {
            StartCaseWaitingForEstimation();

            _sut.Deliver(EstimationCompleted());
            _sut.Deliver(new EstimationCompleted { CaseNumber = _caseNumber, EstimationTicket = "second-copy" });

            Sent<NotifyEstimation>().ShouldHaveSingleItem();
            Data.ExternalEstimationTicket.ShouldBe("estimation");
            _attachments.Deleted.ShouldBe(["second-copy"]);
        }

        // ---- Stage 4: notification ----

        [Fact]
        public void Notification_carries_the_estimation_ticket_and_the_skipped_images()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(new UnableToDownloadImage { CaseNumber = _caseNumber, ImageId = 0, Error = "404" });
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(ImageAnalysed(1));
            _sut.Deliver(EstimationCompleted());

            var notification = Sent<NotifyEstimation>().ShouldHaveSingleItem();
            notification.EstimationTicket.ShouldBe("estimation");
            notification.Attempt.ShouldBe(1);
            var skipped = notification.SkippedImages.ShouldHaveSingleItem();
            skipped.ImageId.ShouldBe(0);
            skipped.Url.ShouldBe("http://images.test/image0.png");
            skipped.Reason.ShouldBe("404");
            Deferred<NotificationTimeout>().ShouldHaveSingleItem().Delay.ShouldBe(EstimationState.NotificationWaitTime);
        }

        [Fact]
        public void State_changes_tell_the_state_holder_the_result_and_the_estimation()
        {
            StartCaseWaitingForEstimation();
            _sut.Deliver(EstimationCompleted());

            var ready = Sent<EstimationStateChanged>().Last();
            ready.State.ShouldBe(EstimationStates.EstimationReady);
            ready.Result.ShouldBe(CaseResults.Success);
            ready.EstimationTicket.ShouldBe("estimation");
            Sent<EstimationStateChanged>().First().Result.ShouldBeNull();
        }

        [Fact]
        public void Failed_case_tells_the_state_holder_why_it_failed()
        {
            StartCaseWaitingForEstimation();
            _sut.Deliver(new UnableToEstimate { CaseNumber = _caseNumber, Error = "503" });

            var error = Sent<EstimationStateChanged>().Last();
            error.State.ShouldBe(EstimationStates.EstimationError);
            error.Result.ShouldBe(CaseResults.Failure);
            error.FailureReason.ShouldBe("503");
        }

        [Fact]
        public void Failed_notification_is_retried_with_a_growing_delay()
        {
            StartCaseWaitingForNotification();

            _sut.Deliver(NotificationFailed(attempt: 1));
            _sut.Deliver(NotificationFailed(attempt: 2));
            _sut.Deliver(NotificationFailed(attempt: 3));

            var retries = _bus.Events.OfType<MessageDeferred<NotifyEstimation>>().ToList();
            retries.Select(x => x.Delay).ShouldBe(
                [TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)]);
            retries.Select(x => x.CommandMessage.Attempt).ShouldBe([2, 3, 4]);
            Data.State.ShouldBe(EstimationStates.EstimationReady);
        }

        [Fact]
        public void Notification_gives_up_after_the_last_attempt()
        {
            StartCaseWaitingForNotification();

            for (var attempt = 1; attempt <= EstimationState.MaxNotificationAttempts; attempt++)
            {
                _sut.Deliver(NotificationFailed(attempt));
            }

            Sent<EstimationStateChanged>().Last().State.ShouldBe(EstimationStates.ClientNotificationError);
            _sut.Data.ShouldBeEmpty();
        }

        [Fact]
        public void Notifier_that_never_answers_is_retried_by_its_timeout()
        {
            StartCaseWaitingForNotification();

            _sut.Deliver(new NotificationTimeout { CaseNumber = _caseNumber, Attempt = 1 });

            _bus.Events.OfType<MessageDeferred<NotifyEstimation>>().ShouldHaveSingleItem()
                .CommandMessage.Attempt.ShouldBe(2);
        }

        [Fact]
        public void Answers_of_an_earlier_attempt_are_ignored()
        {
            StartCaseWaitingForNotification();
            _sut.Deliver(NotificationFailed(attempt: 1));
            _bus.Clear();

            _sut.Deliver(NotificationFailed(attempt: 1));
            _sut.Deliver(new NotificationTimeout { CaseNumber = _caseNumber, Attempt = 1 });

            _bus.Events.OfType<MessageDeferred<NotifyEstimation>>().ShouldBeEmpty();
            Data.NotificationAttempt.ShouldBe(2);
        }

        [Fact]
        public void Client_reached_by_a_slow_attempt_after_a_retry_still_ends_the_case_once()
        {
            StartCaseWaitingForNotification();
            _sut.Deliver(new NotificationTimeout { CaseNumber = _caseNumber, Attempt = 1 });

            _sut.Deliver(new NotificationCompleted { CaseNumber = _caseNumber, Attempt = 1 });
            _sut.Deliver(new NotificationCompleted { CaseNumber = _caseNumber, Attempt = 2 });

            Sent<EstimationStateChanged>().Count(x => x.State == EstimationStates.ClientNotified).ShouldBe(1);
        }

        [Fact]
        public void Finished_case_deletes_images_and_analyses_but_keeps_the_estimation()
        {
            StartCaseWaitingForNotification();

            _sut.Deliver(new NotificationCompleted { CaseNumber = _caseNumber, Attempt = 1 });

            _attachments.Deleted.ShouldBe(["image-0", "metadata-0"], ignoreOrder: true);
            _sut.Data.ShouldBeEmpty();
        }

        [Fact]
        public void Reported_states_only_move_forward()
        {
            StartCase(imageCount: 2);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageDownloaded(1));
            _sut.Deliver(ImageAnalysed(0));
            _sut.Deliver(ImageAnalysed(1));
            _sut.Deliver(EstimationCompleted());

            Sent<EstimationStateChanged>().Select(x => x.State).ShouldBe(
            [
                EstimationStates.WaitingForImagesToBeDownloaded,
                EstimationStates.WaitingForImagesToBeAnalysed,
                EstimationStates.WaitingForEstimation,
                EstimationStates.EstimationReady
            ]);
        }

        // ---- Helpers ----

        private EstimationState Data => _sut.Data.OfType<EstimationState>().Single();

        private List<T> Sent<T>() =>
            _bus.Events.OfType<MessageSent<T>>().Select(x => x.CommandMessage).ToList();

        private List<MessageDeferredToSelf<T>> Deferred<T>() =>
            _bus.Events.OfType<MessageDeferredToSelf<T>>().ToList();

        private void StartCase(int imageCount) => _sut.Deliver(RequestEstimation(imageCount));

        private void StartCaseWaitingForEstimation()
        {
            StartCase(imageCount: 1);
            _sut.Deliver(ImageDownloaded(0));
            _sut.Deliver(ImageAnalysed(0));
            Sent<EstimateCase>().ShouldHaveSingleItem();
        }

        private void StartCaseWaitingForNotification()
        {
            StartCaseWaitingForEstimation();
            _sut.Deliver(EstimationCompleted());
            Sent<NotifyEstimation>().ShouldHaveSingleItem();
        }

        private EstimationRequested RequestEstimation(int imageCount) => new()
        {
            CaseNumber = _caseNumber,
            ClientId = "client",
            CallbackUrl = "http://notification.test",
            ImageUrls = Enumerable.Range(0, imageCount).Select(i => $"http://images.test/image{i}.png").ToArray()
        };

        private ImageDownloaded ImageDownloaded(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId, ImageTicket = $"image-{imageId}" };

        private ImageAnalysisAccepted AnalysisAccepted(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId };

        private ImageAnalysed ImageAnalysed(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId, MetadataTicket = $"metadata-{imageId}" };

        private UnableToAnalyseImage UnableToAnalyse(int imageId) =>
            new() { CaseNumber = _caseNumber, ImageId = imageId, Error = "error" };

        private EstimationCompleted EstimationCompleted() =>
            new() { CaseNumber = _caseNumber, EstimationTicket = "estimation" };

        private UnableToNotify NotificationFailed(int attempt) =>
            new() { CaseNumber = _caseNumber, Attempt = attempt, Error = "client down" };
    }
}
