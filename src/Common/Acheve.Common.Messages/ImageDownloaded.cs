namespace Acheve.Common.Messages
{
    public class ImageDownloaded
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }

        public required string ImageTicket { get; init; }
    }
}