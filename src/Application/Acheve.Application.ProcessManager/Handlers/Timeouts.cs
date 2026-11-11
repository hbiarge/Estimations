namespace Acheve.Application.ProcessManager.Handlers
{
    // Messages the saga defers to itself. Each one checks a wait the saga started when it sent a command,
    // so a command that is never answered can't leave the case waiting forever.

    public class ImageDownloadTimeout
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }
    }

    public class ImageAnalysisTimeout
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }
    }

    public class EstimationTimeout
    {
        public required Guid CaseNumber { get; init; }
    }

    public class NotificationTimeout
    {
        public required Guid CaseNumber { get; init; }

        public required int Attempt { get; init; }
    }
}
