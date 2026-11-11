namespace Acheve.Common.Messages
{
    public class NotifyEstimation
    {
        public required Guid CaseNumber { get; init; }

        public required string CallbackUrl { get; init; }

        public required string EstimationTicket { get; init; }

        // 1 for the first try. The answer carries it back so the saga can tell an old answer from the current one.
        public required int Attempt { get; init; }

        public ICollection<SkippedImage> SkippedImages { get; init; } = [];
    }
}
