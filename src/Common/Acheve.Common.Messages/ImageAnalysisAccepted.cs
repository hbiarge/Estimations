namespace Acheve.Common.Messages
{
    // The external image service accepted the image. The saga's wait for the analysis starts again from here.
    public class ImageAnalysisAccepted
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }
    }
}
