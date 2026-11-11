namespace Acheve.Common.Messages
{
    // An image the estimation was made without, and why.
    public class SkippedImage
    {
        public required int ImageId { get; init; }

        public required string Url { get; init; }

        public required string Reason { get; init; }
    }
}
