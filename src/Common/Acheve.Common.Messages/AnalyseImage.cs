namespace Acheve.Common.Messages
{
    public class AnalyseImage
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }

        public required string ImageTicket { get; init; }

        // Without the dot, for example "png". Empty when the image URL has no extension.
        public required string ImageExtension { get; init; }

        public string ImageName => string.IsNullOrEmpty(ImageExtension)
            ? $"{CaseNumber:D}_{ImageId:G}"
            : $"{CaseNumber:D}_{ImageId:G}.{ImageExtension}";
    }
}