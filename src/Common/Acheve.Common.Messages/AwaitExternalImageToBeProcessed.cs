namespace Acheve.Common.Messages
{
    public class AwaitExternalImageToBeProcessed
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }
    }
}