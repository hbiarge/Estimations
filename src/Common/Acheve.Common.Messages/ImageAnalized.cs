﻿namespace Acheve.Common.Messages
{
    public class ImageAnalized
    {
        public required Guid CaseNumber { get; init; }

        public required int ImageId { get; init; }

        public required string MetadataTicket { get; init; }
    }
}