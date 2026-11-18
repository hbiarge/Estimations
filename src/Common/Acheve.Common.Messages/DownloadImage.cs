namespace Acheve.Common.Messages
{
    public class DownloadImage
    {
        public required Guid CaseNumber { get; init; }

        // The customer the case belongs to, for the worker's logs and spans.
        public required string CustomerId { get; init; }

        public required int ImageId { get; init; }
        
        public required string ImageUrl { get; init; }
    }
}