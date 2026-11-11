namespace Acheve.Application.Api.Features.Estimations
{
    public class EstimationState
    {
        public required string Ticket { get; init; }

        public required string State { get; init; }

        // "Success" or "Failure" once the case has an estimation or has failed; null while it is running.
        public string? Result { get; init; }

        // Why the case failed; null when it didn't.
        public string? FailureReason { get; init; }

        // The estimation, when the case has one and it is still kept (24 hours by default).
        public string? Estimation { get; init; }

        // Images the estimation was made without, and why. Empty until every image has a result.
        public required IReadOnlyList<SkippedImageState> SkippedImages { get; init; }
    }

    public class SkippedImageState
    {
        public required int ImageId { get; init; }

        public required string Url { get; init; }

        public required string Reason { get; init; }
    }
}
