namespace Acheve.Common.Messages
{
    // Event, published once when a case without an estimation ends: the client was told about the failure,
    // or every attempt failed. Any service can subscribe; the process manager doesn't know who listens.
    public class CaseFailed
    {
        public required Guid CaseNumber { get; init; }

        public required string ClientId { get; init; }

        // Why the case failed: no_valid_images, estimation_rejected or estimation_timeout.
        public required string FailureReason { get; init; }

        // The error text, as sent to the client.
        public required string Error { get; init; }

        // Images left out of the estimation.
        public ICollection<SkippedImage> SkippedImages { get; init; } = [];

        // False when every notification attempt failed (final state ClientNotificationError).
        public required bool ClientNotified { get; init; }

        public required DateTimeOffset StartedAt { get; init; }

        public required DateTimeOffset FinishedAt { get; init; }
    }
}
