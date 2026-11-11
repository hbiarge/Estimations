namespace Acheve.Common.Messages
{
    public class EstimationStateChanged
    {
        public required Guid CaseNumber { get; init; }

        public required string ClientId { get; init; }

        public required EstimationStates State { get; init; }

        // A CaseResults value once the case has an estimation or has failed; null before that.
        public string? Result { get; init; }

        // Why the case failed; null when it didn't.
        public string? FailureReason { get; init; }

        // Data bus ticket of the estimation; null when there is none.
        public string? EstimationTicket { get; init; }

        // Images left out of the estimation. Empty until every image has a result.
        public ICollection<SkippedImage> SkippedImages { get; init; } = [];
    }
}
