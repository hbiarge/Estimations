namespace Acheve.Common.Messages
{
    public class DownloadImage
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }
        
        public required string ImageUrl { get; init; }
    }
}