namespace Acheve.Common.Messages
{
    public class ExternalEstimationReady
    {
        public required Guid CaseNumber { get; init; }

        public required ICollection<ImageMetadata> Metadata { get; init; }
    }

    public class ImageMetadata
    {
        public required int ImageId { get; init; }

        public required string Metadata { get; init; }
    }
}