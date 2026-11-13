namespace Acheve.Common.Messages
{
    // Event, published once when a case with an estimation ends: the client was notified, or every attempt failed.
    // Any service can subscribe; the process manager doesn't know who listens.
    public class CaseCompleted
    {
        public required Guid CaseNumber { get; init; }

        public required string ClientId { get; init; }

        // Data bus ticket of the estimation. It stays on the data bus until its retention time is over.
        public required string EstimationTicket { get; init; }

        // Images left out of the estimation.
        public ICollection<SkippedImage> SkippedImages { get; init; } = [];

        // False when every notification attempt failed (final state ClientNotificationError).
        public required bool ClientNotified { get; init; }

        public required DateTimeOffset StartedAt { get; init; }

        public required DateTimeOffset FinishedAt { get; init; }
    }
}
