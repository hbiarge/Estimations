namespace Acheve.Common.Messages
{
    public class AwaitImageToBeProcessed
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }
    }

    public class StillAwaitingImageToBeProcessed
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }
    }
}