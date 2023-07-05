namespace Acheve.Common.Messages
{
    public class UnableToAnalizeImage
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }
        
        public required string Error { get; init; }
    }
}