using System;

namespace Acheve.Common.Messages
{
    public class ImageUrlReceived
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }
        
        public required string ImageUrl { get; init; }
    }
}