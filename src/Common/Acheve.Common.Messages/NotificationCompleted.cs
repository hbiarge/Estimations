namespace Acheve.Common.Messages
{
    public class NotificationCompleted
    {
        public required Guid CaseNumber { get; init; }

        public required int Attempt { get; init; }
    }
}
