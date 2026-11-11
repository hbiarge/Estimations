namespace Acheve.Common.Messages
{
    public class EstimateCase
    {
        public required Guid CaseNumber { get; init; }

        public required ICollection<ImageMetadata> Metadata { get; init; }
    }

    public class ImageMetadata
    {
        public required int ImageId { get; init; }

        // Data bus ticket of the image's analysis.
        public required string MetadataTicket { get; init; }
    }
}
