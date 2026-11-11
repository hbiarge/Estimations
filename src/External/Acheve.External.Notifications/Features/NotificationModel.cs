namespace Acheve.External.Notifications.Features
{
    public class NotificationModel
    {
        public string CaseNumber { get; set; } = string.Empty;

        // "Success" or "Failure".
        public string Result { get; set; } = string.Empty;

        // Only when Result is "Failure".
        public string? FailureReason { get; set; }

        // Only when Result is "Success".
        public string? Estimation { get; set; }

        // Images the estimation was made without, and why.
        public List<SkippedImageModel> SkippedImages { get; set; } = [];
    }

    public class SkippedImageModel
    {
        public int ImageId { get; set; }

        public string Url { get; set; } = string.Empty;

        public string Reason { get; set; } = string.Empty;
    }
}
