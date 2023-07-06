namespace Acheve.Common.Messages
{
    public class ImageReady
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }

        public required string ImageTicket { get; init; }

        public required string ImageExtension { get; init; }

        public string ImageName => $"{CaseNumber:D}_{ImageId:G}.{ImageExtension}";
    }
}